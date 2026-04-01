from __future__ import annotations

from pathlib import Path
import json
import shutil
import urllib.request
import unittest

from companion.meeting_companion.config import CompanionConfig
from companion.meeting_companion.server import CompanionServer
from companion.meeting_companion.storage import SessionStorage


class DummyProcessor:
    def __init__(self) -> None:
        self.items = []

    def enqueue(self, session) -> None:  # noqa: ANN001
        self.items.append(session)

    def pending_count(self) -> int:
        return len(self.items)


class ServerTests(unittest.TestCase):
    def test_health_endpoint_returns_status(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-server"
        if tmp_root.exists():
            shutil.rmtree(tmp_root, ignore_errors=True)
        tmp_root.mkdir(parents=True, exist_ok=True)

        config = CompanionConfig(
            host="127.0.0.1",
            port=0,
            workspace_root=Path.cwd(),
            companion_root=Path.cwd() / "companion",
            meetings_root=tmp_root,
            ffmpeg_path=None,
            summary_provider="fallback",
            ollama_url="http://127.0.0.1:11434",
            ollama_model="qwen2.5:7b-instruct",
            openai_base_url="https://api.openai.com/v1",
            openai_model="gpt-5-mini",
            openai_api_key=None,
            whisper_gpu_model="small",
            whisper_cpu_model="base",
            startup_name="MeetingRecorderCompanion",
        )
        storage = SessionStorage(tmp_root)
        processor = DummyProcessor()
        server = CompanionServer(config, storage, processor)

        try:
            server.start()
            with urllib.request.urlopen(f"http://127.0.0.1:{server.bound_port}/health", timeout=5) as response:
                payload = json.loads(response.read().decode("utf-8"))
            self.assertTrue(payload["ok"])
            self.assertEqual(payload["activeSessions"], 0)
        finally:
            server.stop()
            shutil.rmtree(tmp_root, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
