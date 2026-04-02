from __future__ import annotations

from pathlib import Path
import json
import shutil
import unittest

from companion.meeting_companion.config import load_config
from companion.meeting_companion.models import FinalizedSession, SessionStartRequest, TranscriptSegment
from companion.meeting_companion.pipeline.diarization import SpeakerDiarizationResult
from companion.meeting_companion.pipeline import speaker_id as speaker_id_module


class SpeakerIdentificationTests(unittest.TestCase):
    def test_recurring_speaker_profiles_persist_across_sessions(self) -> None:
        tmp_root = Path.cwd() / "tmp-tests-speaker-id"
        session_one_dir = tmp_root / "2026-04-01" / "meeting-one-090000"
        session_two_dir = tmp_root / "2026-04-02" / "meeting-two-090000"
        session_one_dir.mkdir(parents=True, exist_ok=True)
        session_two_dir.mkdir(parents=True, exist_ok=True)

        original_extractor = speaker_id_module._extract_segment_embedding
        embeddings = {
            ("meeting-one-090000", 0): [1.0, 0.0, 0.0],
            ("meeting-one-090000", 1): [0.99, 0.01, 0.0],
            ("meeting-two-090000", 0): [0.98, 0.02, 0.0],
        }

        def fake_extractor(config, session, segment, index):
            return embeddings.get((session.session_dir.name, index))

        speaker_id_module._extract_segment_embedding = fake_extractor
        try:
            config = load_config()
            config.meetings_root = tmp_root
            config.speaker_id_enabled = True
            config.ffmpeg_path = "ffmpeg"

            diarization_one = SpeakerDiarizationResult(
                status="completed",
                method="role-separated",
                segments=[
                    TranscriptSegment(start=0.0, end=3.0, text="We should update pricing.", speaker=None, source="system-audio"),
                    TranscriptSegment(start=4.0, end=7.0, text="I'll send the deck.", speaker=None, source="system-audio"),
                ],
                participants=[],
            )
            result_one = speaker_id_module.apply_recurring_speaker_identities(
                config,
                _build_session(session_one_dir),
                diarization_one,
            )

            diarization_two = SpeakerDiarizationResult(
                status="completed",
                method="role-separated",
                segments=[
                    TranscriptSegment(start=0.0, end=3.0, text="We approved the rollout.", speaker="Morgan", source="system-audio"),
                ],
                participants=[],
            )
            result_two = speaker_id_module.apply_recurring_speaker_identities(
                config,
                _build_session(session_two_dir),
                diarization_two,
            )

            self.assertEqual(result_one.status, "completed")
            self.assertEqual(result_two.status, "completed")
            self.assertEqual(diarization_two.segments[0].speaker, "Morgan")

            store_path = tmp_root / "speaker-profiles.json"
            payload = json.loads(store_path.read_text(encoding="utf-8"))
            self.assertEqual(len(payload["profiles"]), 1)
            self.assertEqual(payload["profiles"][0]["display_name"], "Morgan")
        finally:
            speaker_id_module._extract_segment_embedding = original_extractor
            shutil.rmtree(tmp_root, ignore_errors=True)


def _build_session(session_dir: Path) -> FinalizedSession:
    artifacts_dir = session_dir / "_session"
    artifacts_dir.mkdir(parents=True, exist_ok=True)
    return FinalizedSession(
        request=SessionStartRequest(
            session_id=f"session-{session_dir.name}",
            platform="zoom",
            meeting_id=session_dir.name,
            title=session_dir.name,
            started_at="2026-04-01T09:00:00Z",
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


if __name__ == "__main__":
    unittest.main()
