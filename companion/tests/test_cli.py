from __future__ import annotations

from pathlib import Path
import json
import shutil
import uuid
import unittest

from companion.meeting_companion.cli import process_session
from companion.meeting_companion.storage import session_artifact_path


class CliProcessingTests(unittest.TestCase):
    @staticmethod
    def _resolve_processed_session_dir(root: Path) -> Path:
        date_dirs = sorted(root.iterdir())
        session_dirs = [child for date_dir in date_dirs if date_dir.is_dir() for child in date_dir.iterdir() if child.is_dir()]
        if len(session_dirs) != 1:
            raise AssertionError(f"Expected exactly one processed session directory, found {len(session_dirs)}")
        return session_dirs[0]

    def test_process_session_handles_generic_windows_manifest(self) -> None:
        tmp_root = Path.cwd() / f"tmp-tests-cli-{uuid.uuid4().hex}"
        session_dir = tmp_root / "2026-03-27" / "zoom-demo-101500"
        session_dir.mkdir(parents=True, exist_ok=True)

        metadata = {
            "sessionId": "session-1",
            "platform": "zoom",
            "meetingId": "zoom-42",
            "title": "Zoom Demo",
            "startedAt": "2026-03-27T20:15:00Z",
            "sourceType": "native-window",
            "exeName": "Zoom",
            "processId": 1234,
            "windowHandle": "0xFFAA",
            "windowTitle": "Zoom Demo",
            "audioCaptureMode": "process_loopback",
            "captureWarnings": ["offline scaffold"],
            "status": "processing",
            "paths": {
                "recording": str(session_dir / "recording.mp4"),
                "mic": str(session_artifact_path(session_dir, "mic.wav")),
                "capture": str(session_artifact_path(session_dir, "capture.json")),
                "transcriptJson": str(session_artifact_path(session_dir, "transcript.json")),
                "transcriptText": str(session_dir / "transcript.txt"),
                "summary": str(session_dir / "summary.md"),
                "log": str(session_artifact_path(session_dir, "app.log")),
            },
        }
        (session_dir / "metadata.json").write_text(json.dumps(metadata), encoding="utf-8")
        (session_dir / "recording.mp4").write_bytes(b"")
        session_artifact_path(session_dir, "mic.wav").parent.mkdir(parents=True, exist_ok=True)
        session_artifact_path(session_dir, "mic.wav").write_bytes(b"")

        try:
            result = process_session(session_dir)
            self.assertEqual(result, 0)
            processed_dir = self._resolve_processed_session_dir(tmp_root)
            self.assertTrue(session_artifact_path(processed_dir, "transcript.json").exists())
            self.assertTrue((processed_dir / "summary.md").exists())
            self.assertTrue((processed_dir / "action-items.json").exists())
            self.assertTrue((processed_dir / "follow-up.md").exists())
            self.assertTrue(session_artifact_path(processed_dir, "speaker-diarization.json").exists())
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)

    def test_process_session_falls_back_to_browser_captions(self) -> None:
        tmp_root = Path.cwd() / f"tmp-tests-cli-browser-{uuid.uuid4().hex}"
        session_dir = tmp_root / "2026-03-28" / "google-meet-demo-121500"
        session_dir.mkdir(parents=True, exist_ok=True)

        metadata = {
            "sessionId": "session-browser",
            "platform": "google-meet",
            "meetingId": "abc-defg-hij",
            "title": "Google Meet Demo",
            "startedAt": "2026-03-28T18:15:00Z",
            "sourceType": "browser-window",
            "exeName": "chrome",
            "processId": 3456,
            "windowHandle": "0xAA11",
            "windowTitle": "Meet - abc-defg-hij - Google Chrome",
            "audioCaptureMode": "system_loopback",
            "captureWarnings": [],
            "status": "processing",
            "paths": {
                "recording": str(session_dir / "recording.mp4"),
                "systemAudio": str(session_artifact_path(session_dir, "system-audio.wav")),
                "mic": str(session_artifact_path(session_dir, "mic.wav")),
                "capture": str(session_artifact_path(session_dir, "capture.json")),
                "transcriptJson": str(session_artifact_path(session_dir, "transcript.json")),
                "transcriptText": str(session_dir / "transcript.txt"),
                "summary": str(session_dir / "summary.md"),
                "log": str(session_artifact_path(session_dir, "app.log")),
            },
        }
        (session_dir / "metadata.json").write_text(json.dumps(metadata), encoding="utf-8")
        (session_dir / "recording.mp4").write_bytes(b"")
        session_artifact_path(session_dir, "system-audio.wav").parent.mkdir(parents=True, exist_ok=True)
        session_artifact_path(session_dir, "system-audio.wav").write_bytes(b"")
        session_artifact_path(session_dir, "mic.wav").write_bytes(b"")
        session_artifact_path(session_dir, "browser-captions.jsonl").write_text(
            "\n".join(
                [
                    json.dumps({"speaker": "Alice", "text": "Can we ship next Tuesday?", "observedAt": "2026-03-28T18:15:02+00:00"}),
                    json.dumps({"speaker": "Bob", "text": "Yes, I will send the update.", "observedAt": "2026-03-28T18:15:06+00:00"}),
                ]
            ),
            encoding="utf-8",
        )

        try:
            result = process_session(session_dir)
            self.assertEqual(result, 0)
            processed_dir = self._resolve_processed_session_dir(tmp_root)
            transcript = json.loads(session_artifact_path(processed_dir, "transcript.json").read_text(encoding="utf-8"))
            self.assertEqual(transcript["model"], "browser-captions")
            self.assertEqual(transcript["segments"][0]["speaker"], "Alice")
            self.assertIn("Bob: Yes, I will send the update.", (processed_dir / "transcript.txt").read_text(encoding="utf-8"))
            diarization = json.loads(session_artifact_path(processed_dir, "speaker-diarization.json").read_text(encoding="utf-8"))
            self.assertEqual(diarization["participants"], ["Alice", "Bob"])
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
