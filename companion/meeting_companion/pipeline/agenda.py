"""Feature 11: Auto-agenda extraction — detect topics discussed and track coverage."""

from __future__ import annotations

import json
import logging
import re

from ..config import CompanionConfig
from ..models import TranscriptResult
from .openai_client import call_openai_json_safe

logger = logging.getLogger(__name__)


def extract_agenda(
    config: CompanionConfig,
    transcript: TranscriptResult,
    meeting_type: str = "general",
) -> dict:
    """Extract agenda topics with timestamps and coverage status.

    Returns {"items": [{"topic", "startSec", "endSec", "covered", "summary"}]}.
    """
    if not transcript.text.strip():
        return {"items": []}

    excerpt = transcript.text[:config.automation_max_transcript_chars].strip()
    segment_context = ""
    if transcript.segments:
        segment_lines = []
        for seg in transcript.segments[:100]:
            speaker = f"{seg.speaker}: " if seg.speaker else ""
            segment_lines.append(f"[{seg.start:.1f}s] {speaker}{seg.text}")
        segment_context = "\n".join(segment_lines)

    prompt = (
        "Extract the main agenda topics discussed in this meeting.\n"
        "For each topic, provide:\n"
        "- topic: a short descriptive name (3-8 words)\n"
        "- startSec: approximate start time in seconds\n"
        "- endSec: approximate end time in seconds\n"
        "- covered: true if the topic was substantively discussed, false if just mentioned\n"
        "- summary: one sentence summarizing what was said about this topic\n\n"
        "Return strict JSON with a top-level key named items.\n"
        f"Meeting type: {meeting_type}\n\n"
    )
    if segment_context:
        prompt += f"Timestamped transcript:\n{segment_context}\n\n"
    else:
        prompt += f"Transcript:\n{excerpt}\n\n"

    fallback = _fallback_extract_agenda(transcript)
    parsed = call_openai_json_safe(
        config,
        "Extract meeting agenda topics. Return JSON only.",
        prompt,
        config.agenda_openai_max_output_tokens,
        fallback={"items": fallback},
    )

    items = parsed.get("items", [])
    validated: list[dict] = []
    for item in items[:config.agenda_max_topics]:
        if not isinstance(item, dict) or not item.get("topic"):
            continue
        validated.append({
            "topic": str(item["topic"]),
            "startSec": float(item.get("startSec", 0)),
            "endSec": float(item.get("endSec", 0)),
            "covered": bool(item.get("covered", True)),
            "summary": str(item.get("summary", "")),
        })

    return {"items": validated}


def _fallback_extract_agenda(transcript: TranscriptResult) -> list[dict]:
    """Simple topic shift detection based on silence gaps and keyword changes."""
    if not transcript.segments:
        return _fallback_from_text(transcript.text)

    topics: list[dict] = []
    current_topic_start = transcript.segments[0].start if transcript.segments else 0.0
    current_words: list[str] = []

    for i, segment in enumerate(transcript.segments):
        current_words.extend(segment.text.lower().split())

        is_gap = False
        if i + 1 < len(transcript.segments):
            gap = transcript.segments[i + 1].start - segment.end
            is_gap = gap > 15.0

        is_last = i == len(transcript.segments) - 1

        if (is_gap or is_last) and current_words:
            topic_text = " ".join(current_words[:20])
            topic_name = _extract_topic_name(topic_text)
            if topic_name:
                topics.append({
                    "topic": topic_name,
                    "startSec": current_topic_start,
                    "endSec": segment.end,
                    "covered": True,
                    "summary": "",
                })
            current_topic_start = transcript.segments[i + 1].start if not is_last else segment.end
            current_words = []

    return topics[:10]


def _fallback_from_text(text: str) -> list[dict]:
    """Extract topics from plain text using paragraph breaks."""
    paragraphs = re.split(r"\n\s*\n", text.strip())
    topics: list[dict] = []
    for para in paragraphs[:10]:
        name = _extract_topic_name(para[:200])
        if name:
            topics.append({
                "topic": name,
                "startSec": 0,
                "endSec": 0,
                "covered": True,
                "summary": "",
            })
    return topics


def _extract_topic_name(text: str) -> str:
    """Extract a short topic name from text."""
    words = text.split()[:8]
    if len(words) < 2:
        return ""
    name = " ".join(words)
    name = re.sub(r"[.!?,;:]+$", "", name).strip()
    return name[:60] if name else ""
