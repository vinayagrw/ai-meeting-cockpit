from __future__ import annotations

import queue
import threading
import json

from ..config import CompanionConfig
from ..models import FinalizedSession
from ..storage import SessionStorage, session_artifact_path
from .automation import build_action_items_with_openai, build_follow_up_markdown
from .diarization import apply_participant_context, build_speaker_diarization, diarization_to_transcript
from .summarizer import build_summarizer
from .transcriber import FasterWhisperTranscriber


class SessionProcessor:
    def __init__(self, config: CompanionConfig, storage: SessionStorage, autostart_worker: bool = True) -> None:
        self.config = config
        self.storage = storage
        self.transcriber = FasterWhisperTranscriber(config)
        self.summarizer = build_summarizer(config)
        self._queue: "queue.Queue[FinalizedSession | None]" = queue.Queue()
        self._worker = None
        if autostart_worker:
            self._worker = threading.Thread(target=self._run, name="meeting-processor", daemon=True)
            self._worker.start()

    def enqueue(self, session: FinalizedSession) -> None:
        self._queue.put(session)

    def pending_count(self) -> int:
        return self._queue.qsize()

    def stop(self) -> None:
        if self._worker is None:
            return
        self._queue.put(None)
        self._worker.join(timeout=self.config.processing_shutdown_join_timeout_seconds)

    def _run(self) -> None:
        while True:
            session = self._queue.get()
            if session is None:
                self._queue.task_done()
                break

            try:
                self.process_session(session)
            finally:
                self._queue.task_done()

    def process_session(self, session: FinalizedSession) -> None:
        self.storage.append_session_log(session, "Starting local processing pipeline")
        speaker_diarization_path = session_artifact_path(session.session_dir, "speaker-diarization.json")
        speaker_diarization_path.parent.mkdir(parents=True, exist_ok=True)

        transcript = self.transcriber.transcribe(session)
        diarization = build_speaker_diarization(session, transcript, self.transcriber)
        if diarization.segments and (not transcript.segments or any(segment.speaker for segment in diarization.segments)):
            transcript = diarization_to_transcript(diarization, model_name=diarization.method)
            self.storage.append_session_log(session, f"Using {diarization.method} speaker transcript")
        else:
            transcript = apply_participant_context(transcript, diarization)
        self.storage.write_transcript(session, transcript.as_dict(), transcript.text)
        speaker_diarization_path.write_text(
            json.dumps(diarization.as_dict(), indent=2),
            encoding="utf-8",
        )
        self.storage.append_session_log(session, "Speaker diarization artifact written")

        summary = self.summarizer.summarize(session.request, transcript)
        self.storage.write_summary(session, summary.markdown)
        session = self.storage.rename_session_for_summary(session, summary.markdown)
        speaker_diarization_path = session_artifact_path(session.session_dir, "speaker-diarization.json")
        action_items = build_action_items_with_openai(self.config, session.request, transcript)
        (session.session_dir / "action-items.json").write_text(json.dumps({"items": action_items}, indent=2), encoding="utf-8")
        (session.session_dir / "follow-up.md").write_text(
            build_follow_up_markdown(session.request, summary.markdown, action_items),
            encoding="utf-8",
        )
        self.storage.append_session_log(session, "Action items and follow-up artifacts written")

        self.storage.update_metadata(
            session,
            {
                "status": "completed",
                "summary": summary.as_dict(),
                "transcript": transcript.as_dict(),
                "folderName": session.session_dir.name,
                "paths": self.storage.build_paths_payload(session),
                "speakerDiarizationPath": str(speaker_diarization_path),
                "actionItemsPath": str(session.session_dir / "action-items.json"),
                "followUpPath": str(session.session_dir / "follow-up.md"),
            },
        )
        self.storage.append_session_log(session, "Processing pipeline finished")
