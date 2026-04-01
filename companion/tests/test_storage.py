from __future__ import annotations

from pathlib import Path
import json
import shutil
import unittest

from companion.meeting_companion.models import SessionStartRequest, StopRequest
from companion.meeting_companion.storage import SessionStorage


class SessionStorageTests(unittest.TestCase):
    def test_session_lifecycle_writes_expected_files(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests"
        test_root = tmp_root / "session-storage-case"
        if test_root.exists():
            shutil.rmtree(test_root, ignore_errors=True)
        test_root.mkdir(parents=True, exist_ok=True)

        try:
            storage = SessionStorage(test_root)
            start_request = SessionStartRequest(
                session_id="session-1",
                platform="google-meet",
                meeting_id="abc-defg-hij",
                title="Weekly Standup",
                started_at="2026-03-27T18:00:00Z",
                browser_tab_id=42,
            )

            storage.start_session(start_request)
            storage.append_chunk("session-1", "main", 0, b"main-bytes")
            storage.append_chunk("session-1", "mic", 0, b"mic-bytes")
            finalized = storage.stop_session(
                "session-1",
                StopRequest(reason="meeting-ended", stopped_at="2026-03-27T18:15:00Z"),
            )

            metadata = json.loads(finalized.metadata_path.read_text(encoding="utf-8"))
            self.assertEqual(metadata["status"], "processing")
            self.assertTrue(finalized.recording_path.exists())
            self.assertTrue(finalized.mic_path.exists())
            self.assertEqual(finalized.recording_path.read_bytes(), b"main-bytes")
            self.assertEqual(finalized.mic_path.read_bytes(), b"mic-bytes")
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)

    def test_session_folder_can_be_renamed_from_summary_and_platform(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests"
        test_root = tmp_root / "session-rename-case"
        if test_root.exists():
            shutil.rmtree(test_root, ignore_errors=True)
        test_root.mkdir(parents=True, exist_ok=True)

        try:
            storage = SessionStorage(test_root)
            start_request = SessionStartRequest(
                session_id="session-rename",
                platform="google-meet",
                meeting_id="rename-demo",
                title="Daily Sync",
                started_at="2026-03-27T18:30:00Z",
                browser_tab_id=7,
            )

            storage.start_session(start_request)
            storage.append_chunk("session-rename", "main", 0, b"main-bytes")
            storage.append_chunk("session-rename", "mic", 0, b"mic-bytes")
            finalized = storage.stop_session(
                "session-rename",
                StopRequest(reason="meeting-ended", stopped_at="2026-03-27T18:45:00Z"),
            )

            storage.write_summary(finalized, "## Overview\nProduct launch checklist and rollout timing were finalized.")
            renamed = storage.rename_session_for_summary(
                finalized,
                "## Overview\nProduct launch checklist and rollout timing were finalized.",
            )

            self.assertTrue(renamed.session_dir.exists())
            self.assertTrue(renamed.session_dir.name.startswith("google-meet-product-launch-checklist-and-rollout-timing-were-finalized-"))

            metadata = json.loads(renamed.metadata_path.read_text(encoding="utf-8"))
            self.assertEqual(metadata["folderName"], renamed.session_dir.name)
            self.assertEqual(metadata["paths"]["summary"], str(renamed.summary_path))
            self.assertTrue(renamed.summary_path.exists())
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
