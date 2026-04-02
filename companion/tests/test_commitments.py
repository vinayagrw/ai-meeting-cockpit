from __future__ import annotations

from pathlib import Path
import json
import shutil
import unittest

from companion.meeting_companion.config import load_config
from companion.meeting_companion.pipeline.commitments import get_open_commitments


class CommitmentTrackerTests(unittest.TestCase):
    def test_get_open_commitments_reads_insights_json(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-commitments"
        session_dir = tmp_root / "2026-04-01" / "google-meet-demo-090000"
        session_dir.mkdir(parents=True, exist_ok=True)
        try:
            (session_dir / "insights.json").write_text(
                json.dumps(
                    {
                        "commitments": {
                            "items": [
                                {
                                    "commitment": "Send revised pricing deck",
                                    "owner": "Avery",
                                    "dueDate": "2026-04-02",
                                    "meetingDate": "2026-04-01T09:00:00Z",
                                    "status": "open",
                                },
                                {
                                    "commitment": "Archive deprecated notes",
                                    "owner": "Blake",
                                    "dueDate": "2026-04-05",
                                    "meetingDate": "2026-04-01T09:00:00Z",
                                    "status": "done",
                                },
                            ]
                        }
                    }
                ),
                encoding="utf-8",
            )

            config = load_config()
            config.meetings_root = tmp_root
            results = get_open_commitments(config, limit=10)

            self.assertEqual(len(results), 1)
            self.assertEqual(results[0]["owner"], "Avery")
            self.assertIn("sessionDir", results[0])
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
