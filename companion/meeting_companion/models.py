from __future__ import annotations

from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any


def _required_string(payload: dict[str, Any], key: str) -> str:
    value = str(payload.get(key, "")).strip()
    if not value:
        raise ValueError(f"Missing required field: {key}")
    return value


@dataclass(slots=True)
class SessionStartRequest:
    session_id: str
    platform: str
    meeting_id: str
    title: str
    started_at: str
    source_type: str = "browser-tab"
    exe_name: str | None = None
    process_id: int | None = None
    window_handle: str | None = None
    window_title: str | None = None
    audio_capture_mode: str | None = None
    capture_warnings: list[str] = field(default_factory=list)
    browser_tab_id: int | None = None
    source_url: str = ""
    participants_hint: str | None = None

    @classmethod
    def from_payload(cls, payload: dict[str, Any]) -> "SessionStartRequest":
        return cls(
            session_id=_required_string(payload, "sessionId"),
            platform=_required_string(payload, "platform"),
            meeting_id=_required_string(payload, "meetingId"),
            title=_required_string(payload, "title"),
            started_at=_required_string(payload, "startedAt"),
            source_type=str(payload.get("sourceType", "browser-tab")),
            exe_name=payload.get("exeName"),
            process_id=payload.get("processId"),
            window_handle=payload.get("windowHandle"),
            window_title=payload.get("windowTitle"),
            audio_capture_mode=payload.get("audioCaptureMode"),
            capture_warnings=list(payload.get("captureWarnings", []) or []),
            browser_tab_id=payload.get("browserTabId"),
            source_url=str(payload.get("sourceUrl", "")),
            participants_hint=payload.get("participantsHint"),
        )

    @classmethod
    def from_metadata(cls, payload: dict[str, Any]) -> "SessionStartRequest":
        return cls(
            session_id=_required_string(payload, "sessionId"),
            platform=_required_string(payload, "platform"),
            meeting_id=_required_string(payload, "meetingId"),
            title=_required_string(payload, "title"),
            started_at=_required_string(payload, "startedAt"),
            source_type=str(payload.get("sourceType", "browser-tab")),
            exe_name=payload.get("exeName"),
            process_id=payload.get("processId"),
            window_handle=payload.get("windowHandle"),
            window_title=payload.get("windowTitle"),
            audio_capture_mode=payload.get("audioCaptureMode"),
            capture_warnings=list(payload.get("captureWarnings", []) or []),
            browser_tab_id=payload.get("browserTabId"),
            source_url=str(payload.get("sourceUrl", "")),
            participants_hint=payload.get("participantsHint"),
        )

    def to_metadata(self) -> dict[str, Any]:
        data = asdict(self)
        data["sessionId"] = data.pop("session_id")
        data["meetingId"] = data.pop("meeting_id")
        data["startedAt"] = data.pop("started_at")
        data["sourceType"] = data.pop("source_type")
        data["exeName"] = data.pop("exe_name")
        data["processId"] = data.pop("process_id")
        data["windowHandle"] = data.pop("window_handle")
        data["windowTitle"] = data.pop("window_title")
        data["audioCaptureMode"] = data.pop("audio_capture_mode")
        data["captureWarnings"] = data.pop("capture_warnings")
        data["browserTabId"] = data.pop("browser_tab_id")
        data["sourceUrl"] = data.pop("source_url")
        data["participantsHint"] = data.pop("participants_hint")
        return data


@dataclass(slots=True)
class StopRequest:
    reason: str
    stopped_at: str

    @classmethod
    def from_payload(cls, payload: dict[str, Any]) -> "StopRequest":
        return cls(
            reason=str(payload.get("reason", "user-stop")),
            stopped_at=_required_string(payload, "stoppedAt"),
        )


@dataclass(slots=True)
class TranscriptSegment:
    start: float
    end: float
    text: str
    speaker: str | None = None
    source: str | None = None

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass(slots=True)
class TranscriptResult:
    status: str
    text: str
    segments: list[TranscriptSegment] = field(default_factory=list)
    language: str | None = None
    model: str | None = None
    error: str | None = None

    def as_dict(self) -> dict[str, Any]:
        return {
            "status": self.status,
            "text": self.text,
            "segments": [asdict(segment) for segment in self.segments],
            "language": self.language,
            "model": self.model,
            "error": self.error,
        }


@dataclass(slots=True)
class SummaryResult:
    status: str
    markdown: str
    provider: str
    error: str | None = None

    def as_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass(slots=True)
class FinalizedSession:
    request: SessionStartRequest
    session_dir: Path
    metadata_path: Path
    recording_path: Path
    system_audio_path: Path | None
    mic_path: Path
    capture_json_path: Path
    log_path: Path
    transcript_json_path: Path
    transcript_txt_path: Path
    summary_path: Path
    live_captions_json_path: Path | None = None
    live_captions_txt_path: Path | None = None
