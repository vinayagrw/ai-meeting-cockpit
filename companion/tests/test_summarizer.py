from __future__ import annotations

import unittest

from companion.meeting_companion.models import SessionStartRequest, TranscriptResult, TranscriptSegment
from companion.meeting_companion.pipeline.summarizer import build_fallback_summary


class SummarizerTests(unittest.TestCase):
    def test_fallback_summary_uses_structured_evidence(self) -> None:
        meeting = SessionStartRequest(
            session_id="session-123",
            platform="google-meet",
            meeting_id="meet-123",
            title="Launch Planning",
            started_at="2026-03-31T14:00:00Z",
        )
        transcript = TranscriptResult(
            status="completed",
            text=(
                "Alex: We agreed to launch on Friday.\n"
                "Priya: Action item, I will send the release notes tomorrow.\n"
                "Sam: The blocker is legal approval.\n"
                "Alex: Open question, do we need a customer email?"
            ),
            segments=[
                TranscriptSegment(start=5.0, end=10.0, speaker="Alex", text="We agreed to launch on Friday."),
                TranscriptSegment(start=11.0, end=18.0, speaker="Priya", text="Action item, I will send the release notes tomorrow."),
                TranscriptSegment(start=19.0, end=24.0, speaker="Sam", text="The blocker is legal approval."),
                TranscriptSegment(start=25.0, end=30.0, speaker="Alex", text="Open question, do we need a customer email?"),
            ],
        )

        markdown = build_fallback_summary(meeting, transcript)

        self.assertIn("## Key Points", markdown)
        self.assertIn("Participants: Alex, Priya, Sam", markdown)
        self.assertIn("We agreed to launch on Friday", markdown)
        self.assertIn("I will send the release notes tomorrow", markdown)
        self.assertIn("legal approval", markdown)
        self.assertIn("customer email", markdown)


if __name__ == "__main__":
    unittest.main()
