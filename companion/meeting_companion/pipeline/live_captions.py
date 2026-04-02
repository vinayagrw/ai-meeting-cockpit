from __future__ import annotations

from datetime import datetime
import json
from pathlib import Path
import subprocess
import time
from typing import Any

from ..config import CompanionConfig
from ..models import FinalizedSession
from ..storage import SessionStorage, resolve_session_file
from .sentiment import analyze_live_segment
from .speaker_id import identify_live_speaker
from .transcriber import FasterWhisperTranscriber


class LiveCaptionStreamer:
    def __init__(self, config: CompanionConfig, storage: SessionStorage) -> None:
        self.config = config
        self.storage = storage
        self.transcriber = FasterWhisperTranscriber(config)
        self._empty_pass_count = 0
        self._last_speaker: str | None = None
        self._last_speaker_similarity: float | None = None
        self._last_tone: str | None = None
        self._last_tone_confidence: float | None = None
        self._last_meta_line: str | None = None
        self._last_enrichment_at = 0.0

    def run(self, session_dir: Path, poll_seconds: float | None = None, window_seconds: int | None = None) -> int:
        poll_interval = poll_seconds if poll_seconds is not None else self.config.live_captions_default_poll_seconds
        capture_window_seconds = window_seconds if window_seconds is not None else self.config.live_captions_default_window_seconds
        session = self.storage.load_session(session_dir)
        output_json = session.live_captions_json_path or session.session_dir / "live-captions.json"
        output_txt = session.live_captions_txt_path or session.session_dir / "live-captions.txt"
        output_json.parent.mkdir(parents=True, exist_ok=True)

        lines: list[str] = []
        stop_seen = False
        while True:
            status = self._read_capture_status(session.capture_json_path)
            if status == "paused":
                update = self._build_payload("paused", lines or ["Captions paused. Resume recording to continue."], self._load_participants(session))
                changed = False
            else:
                update, lines, changed, enrichment = self._generate_fast_update(session, capture_window_seconds, lines)

            update["status"] = status if status in {"paused", "stopped"} else update.get("status", "listening")
            self._write_update(output_json, update)

            if status == "recording" and enrichment is not None and self._should_run_enrichment(changed, enrichment["candidate_line"]):
                enriched_update, lines = self._apply_enrichment(
                    session,
                    capture_window_seconds,
                    update,
                    lines,
                    enrichment["candidate_line"],
                    enrichment["participants"],
                )
                self._write_update(output_json, enriched_update)
                update = enriched_update

            if changed and lines:
                self._append_text(output_txt, lines[-1])

            if status == "stopped":
                if stop_seen:
                    return 0
                stop_seen = True
            else:
                stop_seen = False

            time.sleep(poll_interval)

    def _generate_fast_update(
        self,
        session: FinalizedSession,
        window_seconds: int,
        existing_lines: list[str],
    ) -> tuple[dict[str, Any], list[str], bool, dict[str, Any] | None]:
        participants = self._load_participants(session)
        clip_path = self._prepare_clip(session, window_seconds) if self.config.ffmpeg_path else self._select_direct_source(session)
        if clip_path is None:
            return self._build_payload("waiting", existing_lines or ["Listening for speech..."], participants), existing_lines, False, None

        started_at = time.perf_counter()
        transcript = self.transcriber.transcribe_audio_path_live(clip_path, language_hint=self.config.live_language_hint)
        if transcript.status in {"failed", "unavailable"}:
            error_text = transcript.text or transcript.error or "Live captions unavailable."
            return self._build_payload(transcript.status, existing_lines or [error_text], participants), existing_lines, False, None

        caption_segments = transcript.segments[-self.config.live_captions_recent_segment_count:] if transcript.segments else []
        if not caption_segments:
            self._empty_pass_count += 1
            if self._empty_pass_count >= self.config.live_captions_empty_passes_before_fallback:
                fallback = self.transcriber.transcribe_audio_path(clip_path)
                caption_segments = fallback.segments[-self.config.live_captions_recent_segment_count:] if fallback.segments else []
                if caption_segments:
                    self._empty_pass_count = 0
        else:
            self._empty_pass_count = 0

        if not caption_segments:
            return self._build_payload("waiting", existing_lines or ["Listening for speech..."], participants), existing_lines, False, None

        candidate_line = " ".join(segment.text for segment in caption_segments).strip()
        updated_lines, changed = self._merge_caption_lines(existing_lines, candidate_line)
        speaker_name, speaker_similarity, tone, tone_confidence = self._resolve_cached_enrichment(changed, updated_lines)
        payload = {
            "status": "listening",
            "text": self._compose_display_text(updated_lines[-self.config.live_captions_display_line_count:], participants),
            "lines": updated_lines[-self.config.live_captions_history_line_count:],
            "participants": participants,
            "segments": [segment.as_dict() for segment in caption_segments],
            "speaker": speaker_name,
            "speakerSimilarity": speaker_similarity,
            "tone": tone,
            "toneConfidence": tone_confidence,
            "latencyMs": int((time.perf_counter() - started_at) * 1000),
            "model": transcript.model,
            "updatedAt": datetime.now().isoformat(timespec="seconds"),
        }
        enrichment = None if not candidate_line else {"candidate_line": candidate_line, "participants": participants}
        return payload, updated_lines, changed, enrichment

    def _prepare_clip(self, session: FinalizedSession, window_seconds: int) -> Path | None:
        processing_dir = session.session_dir / "processing" / "live-captions"
        processing_dir.mkdir(parents=True, exist_ok=True)
        clip_path = processing_dir / "caption-tail.wav"

        system_audio = session.system_audio_path if session.system_audio_path and session.system_audio_path.exists() and session.system_audio_path.stat().st_size > 44 else None
        microphone = session.mic_path if session.mic_path.exists() and session.mic_path.stat().st_size > 44 else None

        if system_audio and microphone:
            command = [
                self.config.ffmpeg_path,
                "-y",
                "-sseof",
                f"-{window_seconds}",
                "-i",
                str(system_audio),
                "-sseof",
                f"-{window_seconds}",
                "-i",
                str(microphone),
                "-filter_complex",
                "[0:a][1:a]amix=inputs=2:normalize=0[a]",
                "-map",
                "[a]",
                "-ac",
                str(self.config.live_captions_clip_channels),
                "-ar",
                str(self.config.live_captions_clip_sample_rate),
                str(clip_path),
            ]
        else:
            source = system_audio or microphone
            if source is None:
                return None

            command = [
                self.config.ffmpeg_path,
                "-y",
                "-sseof",
                f"-{window_seconds}",
                "-i",
                str(source),
                "-vn",
                "-ac",
                str(self.config.live_captions_clip_channels),
                "-ar",
                str(self.config.live_captions_clip_sample_rate),
                str(clip_path),
            ]

        try:
            subprocess.run(command, check=True, capture_output=True)
        except Exception:
            return None

        if not clip_path.exists() or clip_path.stat().st_size <= 44:
            return None
        return clip_path

    @staticmethod
    def _select_direct_source(session: FinalizedSession) -> Path | None:
        candidates = [
            session.mic_path,
            session.system_audio_path,
            session.recording_path,
        ]
        for candidate in candidates:
            if candidate and candidate.exists() and candidate.stat().st_size > 44:
                return candidate
        return None

    @staticmethod
    def _read_capture_status(capture_json_path: Path) -> str:
        if not capture_json_path.exists():
            return "recording"
        try:
            payload = json.loads(capture_json_path.read_text(encoding="utf-8"))
        except Exception:
            return "recording"
        return str(payload.get("status", "recording"))

    def _build_payload(self, status: str, lines: list[str], participants: list[str]) -> dict:
        return {
            "status": status,
            "text": LiveCaptionStreamer._compose_display_text(lines[-self.config.live_captions_display_line_count:], participants),
            "lines": lines[-self.config.live_captions_history_line_count:],
            "participants": participants,
            "speaker": self._last_speaker,
            "speakerSimilarity": self._last_speaker_similarity,
            "tone": self._last_tone,
            "toneConfidence": self._last_tone_confidence,
            "updatedAt": datetime.now().isoformat(timespec="seconds"),
        }

    @staticmethod
    def _write_update(output_json: Path, payload: dict) -> None:
        temp_path = output_json.with_suffix(output_json.suffix + ".tmp")
        temp_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        temp_path.replace(output_json)

    @staticmethod
    def _append_text(output_txt: Path, text: str) -> None:
        timestamp = datetime.now().isoformat(timespec="seconds")
        with output_txt.open("a", encoding="utf-8") as handle:
            handle.write(f"[{timestamp}] {text}\n")

    def _merge_caption_lines(self, existing_lines: list[str], candidate_line: str) -> tuple[list[str], bool]:
        normalized = " ".join(candidate_line.split()).strip()
        if not normalized:
            return existing_lines, False

        if not existing_lines:
            return [normalized], True

        lines = existing_lines[:]
        previous = lines[-1]
        if normalized == previous or normalized in previous:
            return lines, False

        if normalized.startswith(previous):
            lines[-1] = normalized
            return lines[-self.config.live_captions_history_line_count:], True

        if previous.startswith(normalized):
            return lines, False

        lines.append(normalized)
        return lines[-self.config.live_captions_history_line_count:], True

    @staticmethod
    def _compose_display_text(lines: list[str], participants: list[str]) -> str:
        caption_text = "\n".join(lines).strip()
        if participants:
            header = f"Participants detected: {', '.join(participants)}"
            return header if not caption_text else f"{header}\n\n{caption_text}"
        return caption_text

    def _identify_speaker_for_live_update(
        self,
        session: FinalizedSession,
        window_seconds: int,
        candidate_line: str,
    ) -> tuple[str, float] | None:
        if not candidate_line or not self.config.ffmpeg_path:
            return None

        if session.system_audio_path and session.system_audio_path.exists() and session.system_audio_path.stat().st_size > 44:
            identity_clip = self._prepare_single_source_clip(session.system_audio_path, session, window_seconds, "speaker-tail-system.wav")
            if identity_clip is not None:
                try:
                    return identify_live_speaker(self.config, identity_clip)
                finally:
                    identity_clip.unlink(missing_ok=True)

        if session.mic_path.exists() and session.mic_path.stat().st_size > 44:
            return ("You", 1.0)

        return None

    def _resolve_cached_enrichment(
        self,
        changed: bool,
        updated_lines: list[str],
    ) -> tuple[str | None, float | None, str | None, float | None]:
        if not updated_lines:
            return None, None, None, None

        latest_line = updated_lines[-1]
        if changed or latest_line != self._last_meta_line:
            return None, None, None, None

        return self._last_speaker, self._last_speaker_similarity, self._last_tone, self._last_tone_confidence

    def _should_run_enrichment(self, changed: bool, candidate_line: str) -> bool:
        if not candidate_line.strip():
            return False

        if changed:
            return True

        if candidate_line != self._last_meta_line:
            return True

        return (time.perf_counter() - self._last_enrichment_at) >= self.config.live_captions_enrichment_cooldown_seconds

    def _apply_enrichment(
        self,
        session: FinalizedSession,
        window_seconds: int,
        payload: dict[str, Any],
        lines: list[str],
        candidate_line: str,
        participants: list[str],
    ) -> tuple[dict[str, Any], list[str]]:
        enriched_payload = dict(payload)
        tone = analyze_live_segment(self.config, candidate_line)
        speaker_name = payload.get("speaker")
        speaker_similarity = payload.get("speakerSimilarity")

        if (time.perf_counter() - self._last_enrichment_at) >= self.config.live_captions_enrichment_cooldown_seconds:
            live_speaker = self._identify_speaker_for_live_update(session, window_seconds, candidate_line)
            if live_speaker is not None:
                speaker_name, speaker_similarity = live_speaker

        enriched_payload["speaker"] = speaker_name
        enriched_payload["speakerSimilarity"] = speaker_similarity
        enriched_payload["tone"] = tone.get("tone")
        enriched_payload["toneConfidence"] = tone.get("confidence")
        enriched_payload["text"] = self._compose_display_text(lines[-self.config.live_captions_display_line_count:], participants)
        enriched_payload["lines"] = lines[-self.config.live_captions_history_line_count:]
        enriched_payload["updatedAt"] = datetime.now().isoformat(timespec="seconds")

        self._last_speaker = speaker_name
        self._last_speaker_similarity = speaker_similarity
        self._last_tone = enriched_payload["tone"]
        self._last_tone_confidence = enriched_payload["toneConfidence"]
        self._last_meta_line = candidate_line
        self._last_enrichment_at = time.perf_counter()
        return enriched_payload, lines

    def _prepare_single_source_clip(
        self,
        source: Path,
        session: FinalizedSession,
        window_seconds: int,
        file_name: str,
    ) -> Path | None:
        processing_dir = session.session_dir / "processing" / "live-captions"
        processing_dir.mkdir(parents=True, exist_ok=True)
        clip_path = processing_dir / file_name
        command = [
            self.config.ffmpeg_path,
            "-y",
            "-sseof",
            f"-{window_seconds}",
            "-i",
            str(source),
            "-vn",
            "-ac",
            "1",
            "-ar",
            "16000",
            str(clip_path),
        ]

        try:
            subprocess.run(command, check=True, capture_output=True)
        except Exception:
            return None

        if not clip_path.exists() or clip_path.stat().st_size <= 44:
            return None
        return clip_path

    @staticmethod
    def _load_participants(session: FinalizedSession) -> list[str]:
        participants: list[str] = []
        participants_path = resolve_session_file(session.session_dir, ["participants.json"])
        if participants_path.exists():
            try:
                payload = json.loads(participants_path.read_text(encoding="utf-8"))
                raw_participants = payload.get("participants", [])
                if isinstance(raw_participants, list):
                    participants.extend(str(item).strip() for item in raw_participants if str(item).strip())
            except (OSError, ValueError, json.JSONDecodeError):
                pass

        hint = str(session.request.participants_hint or "").strip()
        if hint:
            participants.extend(part.strip() for part in hint.replace(";", ",").split(",") if part.strip())

        ordered: list[str] = []
        seen: set[str] = set()
        for participant in participants:
            key = participant.casefold()
            if key in seen:
                continue
            seen.add(key)
            ordered.append(participant)
        return ordered
