from __future__ import annotations

import json
from pathlib import Path
import shutil
import unittest

from companion.meeting_companion.models import FinalizedSession, SessionStartRequest, TranscriptResult
from companion.meeting_companion.pipeline.diarization import (
    SpeakerDiarizationResult,
    apply_participant_context,
    build_speaker_diarization,
)


class _StubTranscriber:
    def transcribe_audio_path(self, path: Path) -> TranscriptResult:
        return TranscriptResult(status="completed", text="", segments=[], language="en", model="stub")


class DiarizationTests(unittest.TestCase):
    def test_build_speaker_diarization_uses_participants_file_when_segments_have_no_names(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-diarization"
        session_dir = tmp_root / "2026-03-29" / "zoom-demo-101500"
        session_dir.mkdir(parents=True, exist_ok=True)
        try:
            (session_dir / "participants.json").write_text(
                json.dumps({"participants": ["Alex", "Priya"]}),
                encoding="utf-8",
            )
            mic_path = session_dir / "mic.wav"
            mic_path.write_bytes(b"0" * 45)
            metadata_path = session_dir / "metadata.json"
            metadata_path.write_text("{}", encoding="utf-8")

            session = FinalizedSession(
                request=SessionStartRequest(
                    session_id="session-1",
                    platform="zoom",
                    meeting_id="demo",
                    title="Demo",
                    started_at="2026-03-29T10:15:00Z",
                ),
                session_dir=session_dir,
                metadata_path=metadata_path,
                recording_path=session_dir / "recording.mp4",
                system_audio_path=None,
                mic_path=mic_path,
                capture_json_path=session_dir / "capture.json",
                log_path=session_dir / "app.log",
                transcript_json_path=session_dir / "transcript.json",
                transcript_txt_path=session_dir / "transcript.txt",
                summary_path=session_dir / "summary.md",
            )

            result = build_speaker_diarization(
                session,
                TranscriptResult(status="completed", text="", segments=[]),
                _StubTranscriber(),
            )

            self.assertIn("Alex", result.participants)
            self.assertIn("Priya", result.participants)
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)

    def test_apply_participant_context_prefixes_transcript(self) -> None:
        transcript = TranscriptResult(status="completed", text="We reviewed the roadmap.", segments=[])
        diarization = SpeakerDiarizationResult(
            status="completed",
            method="named-captions",
            segments=[],
            participants=["Alex", "Priya"],
        )

        enriched = apply_participant_context(transcript, diarization)

        self.assertIn("Participants detected: Alex, Priya", enriched.text)
        self.assertIn("We reviewed the roadmap.", enriched.text)


if __name__ == "__main__":
    unittest.main()
