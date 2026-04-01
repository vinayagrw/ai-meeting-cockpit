from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import json
from pathlib import Path
import re
import threading
import time
from typing import BinaryIO

from .models import FinalizedSession, SessionStartRequest, StopRequest

SESSION_ARTIFACTS_DIRNAME = "_session"


def slugify(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug[:80] or "meeting"


def markdown_to_plain_text(markdown: str) -> str:
    text = markdown.replace("\r", "\n")
    text = re.sub(r"`{1,3}([^`]*)`{1,3}", r"\1", text)
    text = re.sub(r"!\[[^\]]*\]\([^)]+\)", " ", text)
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)
    text = re.sub(r"^\s{0,3}[#>\-\*\+\d\.\)\[\]]+\s*", "", text, flags=re.MULTILINE)
    text = re.sub(r"\|", " ", text)
    text = re.sub(r"\s+", " ", text)
    return text.strip()


def derive_session_folder_label(summary_markdown: str, fallback_title: str) -> str:
    plain_text = markdown_to_plain_text(summary_markdown)
    generic_prefix_pattern = re.compile(
        r"^(overview|summary|meeting summary|recap|notes|highlights|action items|follow up|follow-up)\s*[:\-]?\s*",
        re.IGNORECASE,
    )

    for sentence in re.split(r"(?<=[.!?])\s+|\n+", plain_text):
        candidate = sentence.strip(" -:\t\r\n")
        candidate = generic_prefix_pattern.sub("", candidate).strip(" -:\t\r\n")
        if not candidate:
            continue
        if len(candidate) < 8:
            continue
        return candidate[:120]

    return fallback_title


def parse_timestamp(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone()


def session_artifacts_dir(session_dir: Path) -> Path:
    return session_dir / SESSION_ARTIFACTS_DIRNAME


def session_artifact_path(session_dir: Path, filename: str) -> Path:
    return session_artifacts_dir(session_dir) / filename


def resolve_session_file(session_dir: Path, candidates: list[str], *, prefer_artifacts: bool = True) -> Path:
    search_paths: list[Path] = []
    for candidate in candidates:
        artifact_candidate = session_artifact_path(session_dir, candidate)
        root_candidate = session_dir / candidate
        if prefer_artifacts:
            search_paths.extend([artifact_candidate, root_candidate])
        else:
            search_paths.extend([root_candidate, artifact_candidate])

    for candidate in search_paths:
        if candidate.exists():
            return candidate

    fallback_name = candidates[0]
    return session_artifact_path(session_dir, fallback_name) if prefer_artifacts else session_dir / fallback_name


@dataclass(slots=True)
class ActiveSession:
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
    live_captions_json_path: Path | None
    live_captions_txt_path: Path | None
    recording_handle: BinaryIO
    mic_handle: BinaryIO
    last_sequence: dict[str, int]


class SessionStorage:
    def __init__(self, meetings_root: Path) -> None:
        self.meetings_root = meetings_root
        self._lock = threading.RLock()
        self._active_sessions: dict[str, ActiveSession] = {}

    @property
    def active_count(self) -> int:
        with self._lock:
            return len(self._active_sessions)

    def _build_session_dir(self, request: SessionStartRequest) -> Path:
        started_at = parse_timestamp(request.started_at)
        date_dir = self.meetings_root / started_at.date().isoformat()
        date_dir.mkdir(parents=True, exist_ok=True)

        session_name = f"{request.platform}-{slugify(request.title)}-{started_at:%H%M%S}"
        session_dir = date_dir / session_name
        suffix = 1
        while session_dir.exists():
            session_dir = date_dir / f"{session_name}-{suffix}"
            suffix += 1
        return session_dir

    def _build_session_dir_name(self, request: SessionStartRequest, label: str) -> str:
        started_at = parse_timestamp(request.started_at)
        platform_slug = slugify(request.platform)
        label_slug = slugify(label)
        return f"{platform_slug}-{label_slug}-{started_at:%H%M%S}"

    def _write_json(self, path: Path, payload: dict) -> None:
        path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    def _append_log(self, path: Path, message: str) -> None:
        timestamp = datetime.now().isoformat(timespec="seconds")
        try:
            with path.open("a", encoding="utf-8") as handle:
                handle.write(f"[{timestamp}] {message}\n")
        except PermissionError:
            fallback_path = path.with_name("processing.log")
            try:
                with fallback_path.open("a", encoding="utf-8") as handle:
                    handle.write(f"[{timestamp}] {message}\n")
            except PermissionError:
                return

    def build_paths_payload(self, session: FinalizedSession | ActiveSession) -> dict:
        return {
            "recording": str(session.recording_path),
            "systemAudio": str(session.system_audio_path) if session.system_audio_path else None,
            "mic": str(session.mic_path),
            "capture": str(session.capture_json_path),
            "transcriptJson": str(session.transcript_json_path),
            "transcriptText": str(session.transcript_txt_path),
            "summary": str(session.summary_path),
            "liveCaptionsJson": str(session.live_captions_json_path) if session.live_captions_json_path else None,
            "liveCaptionsText": str(session.live_captions_txt_path) if session.live_captions_txt_path else None,
            "log": str(session.log_path),
        }

    def _build_metadata(self, active: ActiveSession, status: str, stop_request: StopRequest | None = None) -> dict:
        metadata = active.request.to_metadata()
        metadata["status"] = status
        metadata["paths"] = self.build_paths_payload(active)
        if stop_request:
            metadata["stopReason"] = stop_request.reason
            metadata["stoppedAt"] = stop_request.stopped_at
        return metadata

    def start_session(self, request: SessionStartRequest) -> FinalizedSession:
        with self._lock:
            if request.session_id in self._active_sessions:
                raise ValueError(f"Session already exists: {request.session_id}")

            session_dir = self._build_session_dir(request)
            session_dir.mkdir(parents=True, exist_ok=True)
            artifacts_dir = session_artifacts_dir(session_dir)
            artifacts_dir.mkdir(parents=True, exist_ok=True)

            metadata_path = session_dir / "metadata.json"
            recording_path = session_dir / "recording.webm"
            mic_path = artifacts_dir / "mic.webm"
            capture_json_path = artifacts_dir / "capture.json"
            transcript_json_path = artifacts_dir / "transcript.json"
            transcript_txt_path = session_dir / "transcript.txt"
            summary_path = session_dir / "summary.md"
            live_captions_json_path = artifacts_dir / "live-captions.json"
            live_captions_txt_path = artifacts_dir / "live-captions.txt"
            log_path = artifacts_dir / "app.log"

            active = ActiveSession(
                request=request,
                session_dir=session_dir,
                metadata_path=metadata_path,
                recording_path=recording_path,
                system_audio_path=None,
                mic_path=mic_path,
                capture_json_path=capture_json_path,
                log_path=log_path,
                transcript_json_path=transcript_json_path,
                transcript_txt_path=transcript_txt_path,
                summary_path=summary_path,
                live_captions_json_path=live_captions_json_path,
                live_captions_txt_path=live_captions_txt_path,
                recording_handle=recording_path.open("ab"),
                mic_handle=mic_path.open("ab"),
                last_sequence={"main": -1, "mic": -1},
            )

            self._active_sessions[request.session_id] = active
            self._write_json(metadata_path, self._build_metadata(active, "recording"))
            self._write_json(
                capture_json_path,
                {
                    "status": "recording",
                    "sessionId": request.session_id,
                    "platform": request.platform,
                    "sourceType": request.source_type,
                    "exeName": request.exe_name,
                    "processId": request.process_id,
                    "windowHandle": request.window_handle,
                    "windowTitle": request.window_title,
                    "audioCaptureMode": request.audio_capture_mode,
                    "warnings": request.capture_warnings,
                },
            )
            self._append_log(log_path, f"Session started for {request.title} ({request.session_id})")
            return self._to_finalized_session(active)

    def append_chunk(self, session_id: str, track: str, sequence: int, chunk: bytes) -> None:
        if track not in {"main", "mic"}:
            raise ValueError(f"Unsupported track: {track}")

        with self._lock:
            active = self._active_sessions.get(session_id)
            if not active:
                raise KeyError(f"Unknown session: {session_id}")

            handle = active.recording_handle if track == "main" else active.mic_handle
            if sequence <= active.last_sequence[track]:
                self._append_log(active.log_path, f"Ignoring out-of-order {track} chunk #{sequence}")
                return

            handle.write(chunk)
            handle.flush()
            active.last_sequence[track] = sequence

    def stop_session(self, session_id: str, stop_request: StopRequest) -> FinalizedSession:
        with self._lock:
            active = self._active_sessions.pop(session_id, None)
            if not active:
                raise KeyError(f"Unknown session: {session_id}")

            active.recording_handle.close()
            active.mic_handle.close()
            self._write_json(active.metadata_path, self._build_metadata(active, "processing", stop_request))
            capture_payload = {
                "status": "stopped",
                "sessionId": active.request.session_id,
                "platform": active.request.platform,
                "sourceType": active.request.source_type,
                "exeName": active.request.exe_name,
                "processId": active.request.process_id,
                "windowHandle": active.request.window_handle,
                "windowTitle": active.request.window_title,
                "audioCaptureMode": active.request.audio_capture_mode,
                "warnings": active.request.capture_warnings,
                "reason": stop_request.reason,
                "stoppedAt": stop_request.stopped_at,
            }
            self._write_json(active.capture_json_path, capture_payload)
            self._append_log(active.log_path, f"Session stopped ({stop_request.reason})")
            return self._to_finalized_session(active)

    def write_transcript(self, session: FinalizedSession, transcript_payload: dict, text_content: str) -> None:
        self._write_json(session.transcript_json_path, transcript_payload)
        session.transcript_txt_path.write_text(text_content, encoding="utf-8")
        self._append_log(session.log_path, "Transcript artifacts written")

    def write_summary(self, session: FinalizedSession, summary_markdown: str) -> None:
        session.summary_path.write_text(summary_markdown, encoding="utf-8")
        self._append_log(session.log_path, "Summary artifact written")

    def update_metadata(self, session: FinalizedSession, patch: dict) -> None:
        data: dict = {}
        if session.metadata_path.exists():
            data = json.loads(session.metadata_path.read_text(encoding="utf-8"))
        data.update(patch)
        self._write_json(session.metadata_path, data)

    def append_session_log(self, session: FinalizedSession, message: str) -> None:
        self._append_log(session.log_path, message)

    def rename_session_for_summary(self, session: FinalizedSession, summary_markdown: str) -> FinalizedSession:
        with self._lock:
            target_label = derive_session_folder_label(summary_markdown, session.request.title)
            target_name = self._build_session_dir_name(session.request, target_label)
            current_dir = session.session_dir

            if current_dir.name == target_name:
                return session

            target_dir = current_dir.parent / target_name
            suffix = 1
            while target_dir.exists() and target_dir != current_dir:
                target_dir = current_dir.parent / f"{target_name}-{suffix}"
                suffix += 1

            rename_error: Exception | None = None
            for _ in range(4):
                try:
                    current_dir.rename(target_dir)
                    rename_error = None
                    break
                except PermissionError as error:
                    rename_error = error
                    time.sleep(0.2)
                except OSError as error:
                    rename_error = error
                    time.sleep(0.2)

            if rename_error is not None:
                self._append_log(session.log_path, f"Unable to rename session directory: {rename_error}")
                return session

            self._refresh_finalized_session_paths(session, current_dir, target_dir)
            self._rewrite_session_path_references(session)
            self._append_log(session.log_path, f"Session directory renamed to {target_dir.name}")
            return session

    def load_session(self, session_dir: Path) -> FinalizedSession:
        metadata_path = session_dir / "metadata.json"
        if not metadata_path.exists():
            raise FileNotFoundError(f"metadata.json not found in {session_dir}")

        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        request = SessionStartRequest.from_metadata(metadata)
        recording_path = self._resolve_artifact(session_dir, ["recording.mp4", "recording.wav", "recording.webm"])
        system_audio_path = None
        metadata_paths = metadata.get("paths", {})
        configured_system_audio = metadata_paths.get("systemAudio")
        if configured_system_audio:
            system_audio_path = Path(configured_system_audio)
        else:
            fallback_system_audio = self._resolve_artifact(session_dir, ["system-audio.wav", "recording.wav"])
            if fallback_system_audio.exists():
                system_audio_path = fallback_system_audio
        mic_path = self._resolve_artifact(session_dir, ["mic.wav", "mic.webm"])
        capture_json_path = Path(metadata_paths["capture"]) if metadata_paths.get("capture") else self._resolve_artifact(session_dir, ["capture.json"])
        return FinalizedSession(
            request=request,
            session_dir=session_dir,
            metadata_path=metadata_path,
            recording_path=recording_path,
            system_audio_path=system_audio_path,
            mic_path=mic_path,
            capture_json_path=capture_json_path,
            log_path=Path(metadata_paths["log"]) if metadata_paths.get("log") else self._resolve_artifact(session_dir, ["app.log"]),
            transcript_json_path=Path(metadata_paths["transcriptJson"]) if metadata_paths.get("transcriptJson") else self._resolve_artifact(session_dir, ["transcript.json"]),
            transcript_txt_path=session_dir / "transcript.txt",
            summary_path=session_dir / "summary.md",
            live_captions_json_path=Path(metadata_paths["liveCaptionsJson"]) if metadata_paths.get("liveCaptionsJson") else self._resolve_artifact(session_dir, ["live-captions.json"]),
            live_captions_txt_path=Path(metadata_paths["liveCaptionsText"]) if metadata_paths.get("liveCaptionsText") else self._resolve_artifact(session_dir, ["live-captions.txt"]),
        )

    def _resolve_artifact(self, session_dir: Path, candidates: list[str]) -> Path:
        return resolve_session_file(session_dir, candidates)

    def _refresh_finalized_session_paths(self, session: FinalizedSession, old_dir: Path, new_dir: Path) -> None:
        session.session_dir = new_dir
        session.metadata_path = self._relocate_path(session.metadata_path, old_dir, new_dir)
        session.recording_path = self._relocate_path(session.recording_path, old_dir, new_dir)
        session.system_audio_path = self._relocate_path(session.system_audio_path, old_dir, new_dir)
        session.mic_path = self._relocate_path(session.mic_path, old_dir, new_dir)
        session.capture_json_path = self._relocate_path(session.capture_json_path, old_dir, new_dir)
        session.log_path = self._relocate_path(session.log_path, old_dir, new_dir)
        session.transcript_json_path = self._relocate_path(session.transcript_json_path, old_dir, new_dir)
        session.transcript_txt_path = self._relocate_path(session.transcript_txt_path, old_dir, new_dir)
        session.summary_path = self._relocate_path(session.summary_path, old_dir, new_dir)
        session.live_captions_json_path = self._relocate_path(session.live_captions_json_path, old_dir, new_dir)
        session.live_captions_txt_path = self._relocate_path(session.live_captions_txt_path, old_dir, new_dir)

    def _rewrite_session_path_references(self, session: FinalizedSession) -> None:
        if session.metadata_path.exists():
            metadata = json.loads(session.metadata_path.read_text(encoding="utf-8"))
            metadata["paths"] = self.build_paths_payload(session)
            metadata["folderName"] = session.session_dir.name
            self._write_json(session.metadata_path, metadata)

        if session.capture_json_path.exists():
            capture_payload = json.loads(session.capture_json_path.read_text(encoding="utf-8"))
            capture_payload["recordingPath"] = str(session.recording_path)
            capture_payload["systemAudioPath"] = str(session.system_audio_path) if session.system_audio_path else None
            capture_payload["micPath"] = str(session.mic_path)
            capture_payload["liveCaptionsJsonPath"] = str(session.live_captions_json_path) if session.live_captions_json_path else None
            capture_payload["liveCaptionsTextPath"] = str(session.live_captions_txt_path) if session.live_captions_txt_path else None
            self._write_json(session.capture_json_path, capture_payload)

    @staticmethod
    def _relocate_path(path: Path | None, old_dir: Path, new_dir: Path) -> Path | None:
        if path is None:
            return None

        try:
            relative = path.relative_to(old_dir)
            return new_dir / relative
        except ValueError:
            return path

    def _to_finalized_session(self, active: ActiveSession) -> FinalizedSession:
        return FinalizedSession(
            request=active.request,
            session_dir=active.session_dir,
            metadata_path=active.metadata_path,
            recording_path=active.recording_path,
            system_audio_path=active.system_audio_path,
            mic_path=active.mic_path,
            capture_json_path=active.capture_json_path,
            log_path=active.log_path,
            transcript_json_path=active.transcript_json_path,
            transcript_txt_path=active.transcript_txt_path,
            summary_path=active.summary_path,
            live_captions_json_path=active.live_captions_json_path,
            live_captions_txt_path=active.live_captions_txt_path,
        )
