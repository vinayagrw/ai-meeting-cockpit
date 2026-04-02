from __future__ import annotations

from pathlib import Path
import shutil
import unittest

from companion.meeting_companion.config import load_config
from companion.meeting_companion.models import FinalizedSession, SessionStartRequest, TranscriptResult, TranscriptSegment
from companion.meeting_companion.pipeline.live_captions import LiveCaptionStreamer
from companion.meeting_companion.storage import SessionStorage


class _FakeTranscriber:
    def transcribe_audio_path_live(self, _clip_path, language_hint=None):
        return TranscriptResult(
            status="completed",
            text="We decided to ship on Friday.",
            segments=[
                TranscriptSegment(
                    start=1.0,
                    end=3.0,
                    text="We decided to ship on Friday.",
                    speaker="Alex",
                )
            ],
            language=language_hint,
            model="fake-live",
        )

    def transcribe_audio_path(self, _clip_path):
        return TranscriptResult(status="completed", text="", segments=[], model="fake-fallback")


class LiveCaptionTests(unittest.TestCase):
    def test_generate_fast_update_keeps_hot_path_light(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-live-captions"
        session_dir = tmp_root / "2026-04-01" / "google-meet-demo-091500"
        artifacts_dir = session_dir / "_session"
        artifacts_dir.mkdir(parents=True, exist_ok=True)

        for file_name in ("recording.mp4", "_session/system-audio.wav", "mic.wav", "_session/capture.json", "_session/app.log", "_session/transcript.json", "transcript.txt", "summary.md"):
            path = session_dir / file_name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"0" * 64)

        try:
            config = load_config()
            config.live_sentiment_enabled = True
            config.live_sentiment_use_openai = False
            config.ffmpeg_path = "ffmpeg"
            storage = SessionStorage(tmp_root)
            streamer = LiveCaptionStreamer(config, storage)
            streamer.transcriber = _FakeTranscriber()
            streamer._prepare_clip = lambda session, window_seconds: session.session_dir / "dummy.wav"  # type: ignore[method-assign]

            session = FinalizedSession(
                request=SessionStartRequest(
                    session_id="session-1",
                    platform="google-meet",
                    meeting_id="meet-1",
                    title="Planning",
                    started_at="2026-04-01T09:15:00Z",
                ),
                session_dir=session_dir,
                metadata_path=session_dir / "metadata.json",
                recording_path=session_dir / "recording.mp4",
                system_audio_path=artifacts_dir / "system-audio.wav",
                mic_path=artifacts_dir / "mic.wav",
                capture_json_path=artifacts_dir / "capture.json",
                log_path=artifacts_dir / "app.log",
                transcript_json_path=artifacts_dir / "transcript.json",
                transcript_txt_path=session_dir / "transcript.txt",
                summary_path=session_dir / "summary.md",
                live_captions_json_path=artifacts_dir / "live-captions.json",
                live_captions_txt_path=artifacts_dir / "live-captions.txt",
            )

            payload, lines, changed, enrichment = streamer._generate_fast_update(session, 4, [])

            self.assertTrue(changed)
            self.assertIsNone(payload["tone"])
            self.assertIsNotNone(enrichment)
            self.assertEqual(lines[-1], "We decided to ship on Friday.")
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)

    def test_apply_enrichment_sets_speaker_and_tone(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-live-captions-speaker"
        session_dir = tmp_root / "2026-04-01" / "google-meet-demo-091600"
        artifacts_dir = session_dir / "_session"
        artifacts_dir.mkdir(parents=True, exist_ok=True)

        for file_name in ("recording.mp4", "mic.wav", "_session/capture.json", "_session/app.log", "_session/transcript.json", "transcript.txt", "summary.md"):
            path = session_dir / file_name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"test")

        try:
            config = load_config()
            config.live_sentiment_enabled = True
            config.live_sentiment_use_openai = False
            config.ffmpeg_path = "ffmpeg"
            storage = SessionStorage(tmp_root)
            streamer = LiveCaptionStreamer(config, storage)
            streamer.transcriber = _FakeTranscriber()
            streamer._prepare_clip = lambda session, window_seconds: session.session_dir / "dummy.wav"  # type: ignore[method-assign]
            streamer._identify_speaker_for_live_update = lambda session, window_seconds, candidate_line: ("Morgan", 0.93)  # type: ignore[method-assign]

            session = FinalizedSession(
                request=SessionStartRequest(
                    session_id="session-1",
                    platform="google-meet",
                    meeting_id="meet-1",
                    title="Planning",
                    started_at="2026-04-01T09:16:00Z",
                ),
                session_dir=session_dir,
                metadata_path=session_dir / "metadata.json",
                recording_path=session_dir / "recording.mp4",
                system_audio_path=artifacts_dir / "system-audio.wav",
                mic_path=artifacts_dir / "mic.wav",
                capture_json_path=artifacts_dir / "capture.json",
                log_path=artifacts_dir / "app.log",
                transcript_json_path=artifacts_dir / "transcript.json",
                transcript_txt_path=session_dir / "transcript.txt",
                summary_path=session_dir / "summary.md",
                live_captions_json_path=artifacts_dir / "live-captions.json",
                live_captions_txt_path=artifacts_dir / "live-captions.txt",
            )

            payload, lines, changed, enrichment = streamer._generate_fast_update(session, 4, [])
            self.assertIsNotNone(enrichment)
            enriched_payload, enriched_lines = streamer._apply_enrichment(
                session,
                4,
                payload,
                lines,
                enrichment["candidate_line"],
                enrichment["participants"],
            )

            self.assertTrue(changed)
            self.assertEqual(enriched_payload["speaker"], "Morgan")
            self.assertEqual(enriched_lines[-1], "We decided to ship on Friday.")
            self.assertEqual(enriched_payload["tone"], "decisive")
        finally:
            shutil.rmtree(tmp_root, ignore_errors=True)

    def test_live_sentiment_heuristic_skips_openai_classifier(self) -> None:
        config = load_config()
        config.live_sentiment_enabled = True
        config.live_sentiment_use_openai = False

        from companion.meeting_companion.pipeline import sentiment as sentiment_module

        original_classifier = sentiment_module.classify_text
        try:
            def _fail(*args, **kwargs):
                raise AssertionError("classify_text should not be used in heuristic live mode")

            sentiment_module.classify_text = _fail
            result = sentiment_module.analyze_live_segment(config, "We decided to ship on Friday.")
            self.assertEqual(result["tone"], "decisive")
        finally:
            sentiment_module.classify_text = original_classifier


if __name__ == "__main__":
    unittest.main()
