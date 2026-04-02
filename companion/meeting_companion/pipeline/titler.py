"""Feature 1: Smart meeting title generation from transcript content."""

from __future__ import annotations

import re

from ..config import CompanionConfig
from ..models import SessionStartRequest, TranscriptResult
from .openai_client import call_openai_safe


def generate_smart_title(config: CompanionConfig, transcript: TranscriptResult, meeting: SessionStartRequest) -> str:
    """Generate a concise 5-8 word descriptive title from the transcript opening."""
    excerpt = transcript.text[:config.titler_max_transcript_chars].strip()
    if not excerpt:
        return meeting.title

    title = call_openai_safe(
        config,
        "Generate a concise meeting title. Return only the title, no quotes or punctuation around it.",
        (
            "Generate a concise 5-8 word meeting title that captures the main topic.\n"
            "Do not use generic titles like 'Team Meeting' or 'Weekly Sync'.\n"
            "Be specific about the actual content discussed.\n\n"
            f"Platform: {meeting.platform}\n"
            f"Original title: {meeting.title}\n\n"
            f"Transcript opening:\n{excerpt}"
        ),
        config.titler_openai_max_output_tokens,
    )

    if title:
        title = title.strip().strip('"').strip("'").strip()
        if 3 <= len(title) <= 120:
            return title

    return _fallback_title(transcript, meeting)


def _fallback_title(transcript: TranscriptResult, meeting: SessionStartRequest) -> str:
    """Extract a title from the first meaningful sentence."""
    lines = [line.strip() for line in transcript.text[:2000].splitlines() if line.strip()]
    for line in lines[:10]:
        cleaned = line.split("] ", 1)[-1].strip() if "] " in line else line
        cleaned = cleaned.split(": ", 1)[-1].strip() if ": " in cleaned else cleaned
        words = cleaned.split()
        if len(words) >= 4:
            title = " ".join(words[:8])
            title = re.sub(r"[.!?,;:]+$", "", title).strip()
            if title:
                return f"{meeting.platform.capitalize()} - {title}"

    return meeting.title
