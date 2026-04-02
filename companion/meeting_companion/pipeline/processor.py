from __future__ import annotations

import queue
import threading
import json

from ..config import CompanionConfig
from ..models import FinalizedSession
from ..storage import SessionStorage, session_artifact_path
from .agenda import extract_agenda
from .automation import build_action_items_with_openai, build_follow_up_markdown
from .commitments import extract_commitments
from .diarization import apply_participant_context, build_speaker_diarization, diarization_to_transcript
from .highlights import export_highlight_clips, extract_highlights
from .meeting_type import detect_meeting_type
from .sentiment import analyze_sentiment
from .speaker_id import apply_recurring_speaker_identities
from .summarizer import build_summarizer
from .titler import generate_smart_title
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
        speaker_identity_path = session_artifact_path(session.session_dir, "speaker-identities.json")
        speaker_diarization_path.parent.mkdir(parents=True, exist_ok=True)
        # 1. Transcribe audio
        transcript = self.transcriber.transcribe(session)
        diarization = build_speaker_diarization(session, transcript, self.transcriber)
        speaker_identity = apply_recurring_speaker_identities(self.config, session, diarization)
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
        speaker_identity_path.write_text(
            json.dumps(speaker_identity.as_dict(), indent=2),
            encoding="utf-8",
        )
        self.storage.append_session_log(session, "Speaker diarization artifact written")
        if speaker_identity.assignments:
            self.storage.append_session_log(session, f"Recurring speaker identities matched: {len(speaker_identity.assignments)} clusters")

        # 2. Detect meeting type
        meeting_type_result = detect_meeting_type(self.config, transcript, session.request)
        self.storage.append_session_log(session, f"Meeting type detected: {meeting_type_result['type']}")

        # 3. Analyze sentiment
        sentiment_results = analyze_sentiment(self.config, transcript)
        self.storage.append_session_log(session, f"Sentiment analyzed: {len(sentiment_results)} segments")

        # 4. Extract agenda
        agenda_result = extract_agenda(self.config, transcript, meeting_type_result["type"])
        self.storage.append_session_log(session, f"Agenda extracted: {len(agenda_result.get('items', []))} topics")

        # 5. Generate smart title
        smart_title = generate_smart_title(self.config, transcript, session.request)
        self.storage.append_session_log(session, f"Smart title: {smart_title}")

        # 6. Summarize (existing, uses meeting type context)
        summary = self.summarizer.summarize(session.request, transcript)
        self.storage.write_summary(session, summary.markdown)
        session = self.storage.rename_session_for_summary(session, summary.markdown)
        speaker_diarization_path = session_artifact_path(session.session_dir, "speaker-diarization.json")
        insights_path = session.session_dir / "insights.json"

        # 7. Extract action items (existing)
        action_items = build_action_items_with_openai(self.config, session.request, transcript)
        (session.session_dir / "action-items.json").write_text(json.dumps({"items": action_items}, indent=2), encoding="utf-8")

        # 8. Extract commitments
        commitments = extract_commitments(self.config, transcript, session.request)
        self.storage.append_session_log(session, f"Commitments extracted: {len(commitments)} items")

        # 9. Extract highlights
        highlights_result = extract_highlights(self.config, transcript, summary.markdown, agenda_result)
        self.storage.append_session_log(session, f"Highlights extracted: {len(highlights_result.get('highlights', []))} moments")
        highlight_clips_result = export_highlight_clips(self.config, session, highlights_result)
        if highlight_clips_result.get("clips"):
            self.storage.append_session_log(session, f"Highlight clips exported: {len(highlight_clips_result.get('clips', []))} clips")

        insights_payload = {
            "smartTitle": smart_title,
            "meetingType": meeting_type_result,
            "sentiment": {"segments": sentiment_results},
            "agenda": agenda_result,
            "highlights": highlights_result,
            "highlightClips": highlight_clips_result,
            "speakerIdentity": speaker_identity.as_dict(),
            "commitments": {"items": commitments},
        }
        insights_path.write_text(json.dumps(insights_payload, indent=2), encoding="utf-8")
        self.storage.append_session_log(session, "Insights artifact written")

        # 10. Generate follow-up
        (session.session_dir / "follow-up.md").write_text(
            build_follow_up_markdown(session.request, summary.markdown, action_items),
            encoding="utf-8",
        )
        self.storage.append_session_log(session, "Action items, commitments, highlights, and follow-up written")

        # 11. Index embeddings for cross-meeting search
        try:
            from ..embeddings.indexer import index_session
            from ..embeddings.store import EmbeddingStore
            store = EmbeddingStore(self.config.meetings_root / "embeddings.db")
            indexed = index_session(self.config, store, session.session_dir)
            store.close()
            if indexed:
                self.storage.append_session_log(session, f"Indexed {indexed} embedding chunks")
        except Exception:
            self.storage.append_session_log(session, "Embedding indexing skipped (no API key or error)")

        # 12. Write final metadata
        self.storage.update_metadata(
            session,
            {
                "status": "completed",
                "summary": summary.as_dict(),
                "transcript": transcript.as_dict(),
                "folderName": session.session_dir.name,
                "smartTitle": smart_title,
                "meetingType": meeting_type_result,
                "paths": self.storage.build_paths_payload(session),
                "speakerDiarizationPath": str(speaker_diarization_path),
                "speakerIdentityPath": str(speaker_identity_path),
                "insightsPath": str(insights_path),
                "actionItemsPath": str(session.session_dir / "action-items.json"),
                "followUpPath": str(session.session_dir / "follow-up.md"),
            },
        )
        self.storage.append_session_log(session, "Processing pipeline finished")
