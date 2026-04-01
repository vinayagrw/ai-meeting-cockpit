from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import json
from pathlib import Path

from ..models import FinalizedSession, TranscriptResult, TranscriptSegment
from ..storage import resolve_session_file


@dataclass(slots=True)
class SpeakerDiarizationResult:
    status: str
    method: str
    segments: list[TranscriptSegment]
    participants: list[str]
    error: str | None = None

    def as_dict(self) -> dict:
        return {
            "status": self.status,
            "method": self.method,
            "participants": self.participants,
            "segments": [segment.as_dict() for segment in self.segments],
            "error": self.error,
        }


def build_speaker_diarization(session: FinalizedSession, transcript: TranscriptResult, transcriber) -> SpeakerDiarizationResult:
    detected_participants = _load_detected_participants(session)
    named_caption_segments, method = _load_named_caption_segments(session)
    if named_caption_segments:
        aligned_segments = _align_named_captions_to_transcript(transcript, named_caption_segments)
        segments = aligned_segments or named_caption_segments
        participants = _merge_participants(detected_participants, [segment.speaker for segment in segments if segment.speaker])
        return SpeakerDiarizationResult(
            status="completed",
            method=f"{method}-aligned" if aligned_segments else method,
            segments=segments,
            participants=participants,
        )

    role_segments = _build_role_separated_segments(session, transcriber)
    if role_segments:
        participants = _merge_participants(detected_participants, [segment.speaker for segment in role_segments if segment.speaker])
        return SpeakerDiarizationResult(
            status="completed",
            method="role-separated",
            segments=role_segments,
            participants=participants,
        )

    return SpeakerDiarizationResult(
        status="unavailable",
        method="none",
        segments=[],
        participants=detected_participants,
        error="No caption or diarization sources were available.",
    )


def diarization_to_transcript(diarization: SpeakerDiarizationResult, model_name: str | None = None) -> TranscriptResult:
    return TranscriptResult(
        status="completed" if diarization.segments else diarization.status,
        text=_build_text(diarization.segments, diarization.participants),
        segments=diarization.segments,
        language="en" if diarization.segments else None,
        model=model_name or diarization.method,
        error=diarization.error,
    )


def apply_participant_context(transcript: TranscriptResult, diarization: SpeakerDiarizationResult) -> TranscriptResult:
    if not diarization.participants:
        return transcript

    participants_line = f"Participants detected: {', '.join(diarization.participants)}"
    if participants_line in transcript.text:
        return transcript

    updated_text = participants_line if not transcript.text.strip() else f"{participants_line}\n\n{transcript.text.strip()}"
    return TranscriptResult(
        status=transcript.status,
        text=updated_text,
        segments=transcript.segments,
        language=transcript.language,
        model=transcript.model,
        error=transcript.error,
    )


def _load_named_caption_segments(session: FinalizedSession) -> tuple[list[TranscriptSegment], str]:
    for filename, method in (("browser-captions.jsonl", "browser-captions"), ("native-captions.jsonl", "native-captions")):
        path = resolve_session_file(session.session_dir, [filename])
        if not path.exists():
            continue

        segments = _parse_caption_jsonl(path)
        if segments:
            return segments, method

    return [], "none"


def _parse_caption_jsonl(path: Path) -> list[TranscriptSegment]:
    events: list[tuple[datetime | None, str | None, str]] = []
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line:
            continue

        try:
            payload = json.loads(line)
        except json.JSONDecodeError:
            continue

        text = str(payload.get("text", "")).strip()
        if not text:
            continue

        speaker = str(payload.get("speaker", "")).strip() or None
        observed_at_raw = str(payload.get("observedAt", "")).strip()
        observed_at = None
        if observed_at_raw:
            try:
                observed_at = datetime.fromisoformat(observed_at_raw.replace("Z", "+00:00"))
            except ValueError:
                observed_at = None

        events.append((observed_at, speaker, text))

    if not events:
        return []

    base_time = next((observed_at for observed_at, _, _ in events if observed_at is not None), None)
    segments: list[TranscriptSegment] = []
    for index, (observed_at, speaker, text) in enumerate(events):
        if observed_at is not None and base_time is not None:
            start = max((observed_at - base_time).total_seconds(), 0.0)
        else:
            start = float(index * 3)

        end = start + 3.0
        next_observed_at = events[index + 1][0] if index + 1 < len(events) else None
        if observed_at is not None and next_observed_at is not None:
            end = start + min(max((next_observed_at - observed_at).total_seconds(), 1.2), 6.0)

        segments.append(
            TranscriptSegment(
                start=start,
                end=end,
                text=text,
                speaker=speaker,
                source=path.stem,
            )
        )

    return segments


def _align_named_captions_to_transcript(transcript: TranscriptResult, caption_segments: list[TranscriptSegment]) -> list[TranscriptSegment]:
    if not transcript.segments:
        return []

    aligned: list[TranscriptSegment] = []
    assigned_count = 0
    for segment in transcript.segments:
        best_match = None
        best_score = 999.0
        for caption in caption_segments:
            overlap = min(segment.end, caption.end) - max(segment.start, caption.start)
            distance = min(abs(segment.start - caption.start), abs(segment.end - caption.end))
            if overlap > 0:
                distance = 0.0

            if distance <= 4.0 and distance < best_score:
                best_score = distance
                best_match = caption

        speaker = best_match.speaker if best_match and best_match.speaker else segment.speaker
        if speaker:
            assigned_count += 1
        aligned.append(
            TranscriptSegment(
                start=segment.start,
                end=segment.end,
                text=segment.text,
                speaker=speaker,
                source=best_match.source if best_match else segment.source,
            )
        )

    return aligned if assigned_count > 0 else []


def _build_role_separated_segments(session: FinalizedSession, transcriber) -> list[TranscriptSegment]:
    segments: list[TranscriptSegment] = []
    if session.system_audio_path and session.system_audio_path.exists() and session.system_audio_path.stat().st_size > 44:
        remote_result = transcriber.transcribe_audio_path(session.system_audio_path)
        segments.extend(
            TranscriptSegment(
                start=segment.start,
                end=segment.end,
                text=segment.text,
                speaker="Meeting",
                source="system-audio",
            )
            for segment in remote_result.segments
        )

    if session.mic_path.exists() and session.mic_path.stat().st_size > 44:
        local_result = transcriber.transcribe_audio_path(session.mic_path)
        segments.extend(
            TranscriptSegment(
                start=segment.start,
                end=segment.end,
                text=segment.text,
                speaker="You",
                source="microphone",
            )
            for segment in local_result.segments
        )

    return sorted(segments, key=lambda segment: (segment.start, segment.end))


def _build_text(segments: list[TranscriptSegment], participants: list[str] | None = None) -> str:
    lines: list[str] = []
    if participants:
        lines.append(f"Participants detected: {', '.join(participants)}")
        lines.append("")
    for segment in segments:
        content = f"{segment.speaker}: {segment.text}" if segment.speaker else segment.text
        lines.append(f"[{_format_timestamp(segment.start)} - {_format_timestamp(segment.end)}] {content}")
    return "\n".join(lines)


def _format_timestamp(value: float) -> str:
    total_seconds = int(value)
    hours = total_seconds // 3600
    minutes = (total_seconds % 3600) // 60
    seconds = total_seconds % 60
    milliseconds = int((value - total_seconds) * 1000)
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}.{milliseconds:03d}"


def _load_detected_participants(session: FinalizedSession) -> list[str]:
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

    return _merge_participants(participants, [])


def _merge_participants(explicit_participants: list[str], segment_participants: list[str | None]) -> list[str]:
    merged: list[str] = []
    seen: set[str] = set()
    for participant in [*explicit_participants, *(value for value in segment_participants if value)]:
        normalized = str(participant).strip()
        if not normalized:
            continue

        key = normalized.casefold()
        if key in seen:
            continue
        seen.add(key)
        merged.append(normalized)

    return sorted(merged, key=str.casefold)
