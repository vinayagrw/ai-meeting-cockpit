"""Feature 5: Meeting type detection — classify meetings to tailor downstream outputs."""

from __future__ import annotations

import re

from ..config import CompanionConfig
from ..models import SessionStartRequest, TranscriptResult
from .classifier import classify_text

MEETING_TYPES = ["standup", "1on1", "interview", "brainstorm", "review", "presentation", "general"]

_TYPE_PATTERNS: dict[str, list[str]] = {
    "standup": ["blocker", "blocked", "yesterday", "today", "standup", "stand-up", "daily", "sprint", "scrum"],
    "1on1": ["one on one", "1:1", "1-on-1", "career", "feedback", "growth", "check-in", "how are you"],
    "interview": ["candidate", "experience", "tell me about", "resume", "hiring", "role", "position", "behavioral"],
    "brainstorm": ["brainstorm", "idea", "ideation", "creative", "whiteboard", "concept", "explore", "what if"],
    "review": ["review", "sprint review", "code review", "pull request", "demo", "retrospective", "retro", "postmortem"],
    "presentation": ["slide", "presentation", "deck", "agenda", "next slide", "overview", "walkthrough"],
    "general": [],
}


def detect_meeting_type(
    config: CompanionConfig,
    transcript: TranscriptResult,
    meeting: SessionStartRequest,
) -> dict:
    """Detect meeting type. Returns {"type": str, "confidence": float}."""
    excerpt = transcript.text[:6000].strip()
    if not excerpt:
        return {"type": "general", "confidence": 0.1}

    detected_type = classify_text(
        config,
        excerpt,
        MEETING_TYPES,
        "Classify this meeting transcript into one category based on its format and content. Return only the label.",
        max_tokens=config.meeting_type_openai_max_output_tokens,
        fallback_patterns=_TYPE_PATTERNS,
    )

    confidence = 0.85 if config.openai_api_key else _compute_fallback_confidence(excerpt, detected_type)

    return {"type": detected_type, "confidence": round(confidence, 2)}


def _compute_fallback_confidence(text: str, detected_type: str) -> float:
    """Score confidence based on keyword density."""
    patterns = _TYPE_PATTERNS.get(detected_type, [])
    if not patterns:
        return 0.2

    text_lower = text.lower()
    hits = sum(1 for p in patterns if re.search(r"\b" + re.escape(p) + r"\b", text_lower))
    return min(0.3 + hits * 0.1, 0.75)
