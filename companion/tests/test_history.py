from __future__ import annotations

from pathlib import Path
import json
import shutil
import unittest

from companion.meeting_companion.config import load_config
from companion.meeting_companion.history import ask_meeting_question, search_meetings
from companion.meeting_companion.storage import SessionStorage


class HistoryTests(unittest.TestCase):
    def test_search_meetings_finds_matching_session(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-history"
        session_dir = tmp_root / "2026-03-28" / "google-meet-demo-120000"
        session_dir.mkdir(parents=True, exist_ok=True)
        try:
            metadata = {
                "sessionId": "session-1",
                "platform": "google-meet",
                "meetingId": "demo",
                "title": "Design Review",
                "startedAt": "2026-03-28T17:00:00Z",
                "status": "completed",
            }
            (session_dir / "metadata.json").write_text(json.dumps(metadata), encoding="utf-8")
            (session_dir / "transcript.txt").write_text("We discussed the dashboard redesign and next sprint.", encoding="utf-8")
            (session_dir / "summary.md").write_text("## Overview\nDashboard redesign review.", encoding="utf-8")

            storage = SessionStorage(tmp_root)
            results = search_meetings(storage, "dashboard")
            self.assertEqual(len(results), 1)
            self.assertIn("Design Review", results[0]["title"])
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)

    def test_ask_meeting_question_falls_back_to_relevant_excerpts_before_summary(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-history-ask"
        session_dir = tmp_root / "2026-03-28" / "teams-demo-121500"
        session_dir.mkdir(parents=True, exist_ok=True)
        try:
            metadata = {
                "sessionId": "session-2",
                "platform": "teams",
                "meetingId": "demo",
                "title": "Planning",
                "startedAt": "2026-03-28T18:15:00Z",
                "status": "completed",
            }
            (session_dir / "metadata.json").write_text(json.dumps(metadata), encoding="utf-8")
            (session_dir / "summary.md").write_text("We agreed to ship the update next Tuesday.", encoding="utf-8")
            (session_dir / "transcript.txt").write_text(
                "[00:00:05.000 - 00:00:10.000] Alex: We will ship the update next Tuesday.\n"
                "[00:00:11.000 - 00:00:15.000] Priya: QA will validate it on Monday.",
                encoding="utf-8",
            )
            (session_dir / "transcript.json").write_text(
                json.dumps(
                    {
                        "status": "completed",
                        "segments": [
                            {"start": 5.0, "end": 10.0, "speaker": "Alex", "text": "We will ship the update next Tuesday."},
                            {"start": 11.0, "end": 15.0, "speaker": "Priya", "text": "QA will validate it on Monday."},
                        ],
                    }
                ),
                encoding="utf-8",
            )

            config = load_config()
            config.openai_api_key = None
            answer = ask_meeting_question(config, session_dir, "When will we ship?")
            self.assertIn("Tuesday", answer)
            self.assertIn("Alex", answer)
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
