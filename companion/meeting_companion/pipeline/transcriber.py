from __future__ import annotations

from pathlib import Path
import shutil
import subprocess
from typing import Iterable

from ..config import CompanionConfig
from ..models import FinalizedSession, TranscriptResult, TranscriptSegment

try:
    from faster_whisper import WhisperModel
except ImportError:  # pragma: no cover - optional dependency
    WhisperModel = None


def _format_timestamp(value: float) -> str:
    total_seconds = int(value)
    hours = total_seconds // 3600
    minutes = (total_seconds % 3600) // 60
    seconds = total_seconds % 60
    milliseconds = int((value - total_seconds) * 1000)
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}.{milliseconds:03d}"


class FasterWhisperTranscriber:
    def __init__(self, config: CompanionConfig) -> None:
        self.config = config
        self._model = None
        self._model_name: str | None = None
        self._live_model = None
        self._live_model_name: str | None = None

    def _detect_device(self) -> tuple[str, str]:
        if shutil.which("nvidia-smi"):
            return "cuda", "float16"
        return "cpu", "int8"

    def _get_model(self):
        if WhisperModel is None:
            return None
        if self._model is None:
            device, compute_type = self._detect_device()
            model_name = self.config.whisper_gpu_model if device == "cuda" else self.config.whisper_cpu_model
            self._model_name = model_name
            self._model = WhisperModel(model_name, device=device, compute_type=compute_type)
        return self._model

    def _get_live_model(self):
        if WhisperModel is None:
            return None
        if self._live_model is None:
            device, compute_type = self._detect_device()
            live_model_name = self.config.whisper_live_gpu_model if device == "cuda" else self.config.whisper_live_cpu_model
            try:
                self._live_model = WhisperModel(live_model_name, device=device, compute_type=compute_type)
                self._live_model_name = live_model_name
            except Exception:
                self._live_model = self._get_model()
                self._live_model_name = self._model_name
        return self._live_model

    def _prepare_audio_candidates(self, session: FinalizedSession) -> list[tuple[str, Path]]:
        raw_candidates = [
            ("recording", session.recording_path),
            ("system-audio", session.system_audio_path) if session.system_audio_path else None,
            ("microphone", session.mic_path),
        ]
        available = [
            (label, candidate)
            for item in raw_candidates
            if item is not None
            for label, candidate in [item]
            if candidate.exists() and candidate.stat().st_size
        ]

        if not available:
            return [("recording", session.recording_path)]

        if not self.config.ffmpeg_path:
            return available

        normalized_dir = session.session_dir / "processing"
        normalized_dir.mkdir(parents=True, exist_ok=True)
        prepared: list[tuple[str, Path]] = []
        for label, source in available:
            normalized_path = normalized_dir / f"{label}-normalized.wav"
            command = [
                self.config.ffmpeg_path,
                "-y",
                "-i",
                str(source),
                "-vn",
                "-ac",
                str(self.config.normalized_channels),
                "-ar",
                str(self.config.normalized_sample_rate),
                str(normalized_path),
            ]

            try:
                subprocess.run(command, check=True, capture_output=True)
                prepared.append((label, normalized_path))
            except Exception:
                prepared.append((label, source))

        return prepared

    def _transcribe_once(self, model, source_audio: Path, *, vad_filter: bool, beam_size: int):
        raw_segments, info = model.transcribe(
            str(source_audio),
            vad_filter=vad_filter,
            beam_size=beam_size,
            task=self.config.full_transcription_task,
        )
        segments = [
            TranscriptSegment(start=segment.start, end=segment.end, text=segment.text.strip())
            for segment in raw_segments
            if segment.text and segment.text.strip()
        ]
        return segments, info

    def _build_text(self, segments: Iterable[TranscriptSegment]) -> str:
        lines = []
        for segment in segments:
            content = segment.text
            if segment.speaker:
                content = f"{segment.speaker}: {content}"
            lines.append(f"[{_format_timestamp(segment.start)} - {_format_timestamp(segment.end)}] {content}")
        return "\n".join(lines)

    def transcribe_audio_path(self, source_audio: Path) -> TranscriptResult:
        model = self._get_model()
        if model is None:
            return TranscriptResult(
                status="unavailable",
                text="Transcription unavailable because faster-whisper is not installed.",
                error="faster-whisper is not installed",
            )

        try:
            for attempt in (
                {"vad_filter": True, "beam_size": self.config.vad_beam_size},
                {"vad_filter": False, "beam_size": self.config.fallback_beam_size},
            ):
                segments, info = self._transcribe_once(model, source_audio, **attempt)
                if segments:
                    return TranscriptResult(
                        status="completed",
                        text=self._build_text(segments),
                        segments=segments,
                        language=getattr(info, "language", None),
                        model=self._model_name,
                    )

            return TranscriptResult(status="completed", text="", segments=[], model=self._model_name)
        except Exception as error:  # pragma: no cover - depends on optional runtime
            return TranscriptResult(
                status="failed",
                text=f"Transcription failed: {error}",
                error=str(error),
            )

    def transcribe_audio_path_live(self, source_audio: Path, language_hint: str | None = "en") -> TranscriptResult:
        model = self._get_live_model()
        if model is None:
            return TranscriptResult(
                status="unavailable",
                text="Transcription unavailable because faster-whisper is not installed.",
                error="faster-whisper is not installed",
            )

        try:
            raw_segments, info = model.transcribe(
                str(source_audio),
                beam_size=self.config.live_beam_size,
                best_of=self.config.live_best_of,
                vad_filter=False,
                condition_on_previous_text=False,
                language=language_hint or self.config.live_language_hint or None,
                task=self.config.live_transcription_task,
                temperature=0.0,
            )
            segments = [
                TranscriptSegment(start=segment.start, end=segment.end, text=segment.text.strip())
                for segment in raw_segments
                if segment.text and segment.text.strip()
            ]
            return TranscriptResult(
                status="completed",
                text=self._build_text(segments),
                segments=segments,
                language=getattr(info, "language", None),
                model=self._live_model_name,
            )
        except Exception as error:  # pragma: no cover - depends on optional runtime
            return TranscriptResult(
                status="failed",
                text=f"Transcription failed: {error}",
                error=str(error),
            )

    def transcribe(self, session: FinalizedSession) -> TranscriptResult:
        audio_candidates = self._prepare_audio_candidates(session)
        best_result: TranscriptResult | None = None
        for _, source_audio in audio_candidates:
            result = self.transcribe_audio_path(source_audio)
            if result.segments:
                return result
            if best_result is None:
                best_result = result

        return best_result or TranscriptResult(
            status="completed",
            text="",
            segments=[],
            model=self._model_name,
        )
