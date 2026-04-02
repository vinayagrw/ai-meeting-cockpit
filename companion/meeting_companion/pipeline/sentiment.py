"""Feature 3: Sentiment/tone analysis — real-time and post-processing."""

from __future__ import annotations

import re

from ..config import CompanionConfig
from ..models import TranscriptResult
from .classifier import classify_segments, classify_text

TONE_CATEGORIES = ["neutral", "positive", "tense", "decisive", "question"]

_TONE_PATTERNS: dict[str, list[str]] = {
    "positive": ["great", "awesome", "excellent", "agree", "love", "perfect", "nice", "congratulations", "well done", "thank"],
    "tense": ["disagree", "concern", "worried", "problem", "issue", "frustrated", "blocker", "delay", "push back", "not acceptable"],
    "decisive": ["decided", "agreed", "approved", "confirmed", "go with", "ship it", "let's do", "final", "resolved"],
    "question": ["what", "how", "why", "when", "where", "who", "could you", "can we", "should we", "do you think"],
    "neutral": [],
}


def analyze_sentiment(
    config: CompanionConfig,
    transcript: TranscriptResult,
) -> list[dict]:
    """Batch analyze sentiment for all transcript segments.

    Returns [{"startSec", "endSec", "tone", "confidence"}].
    """
    if not transcript.segments:
        return []

    segments = [
        {
            "start": seg.start,
            "end": seg.end,
            "text": seg.text,
            "speaker": seg.speaker,
        }
        for seg in transcript.segments
        if seg.text.strip()
    ]

    return classify_segments(
        config,
        segments,
        TONE_CATEGORIES,
        "Classify each line's emotional tone/sentiment. Return only the label per line.",
        max_tokens=config.sentiment_openai_max_output_tokens,
        fallback_patterns=_TONE_PATTERNS,
    )


def analyze_live_segment(config: CompanionConfig, text: str) -> dict:
    """Quick single-segment tone classification for live captions.

    Returns {"tone": str, "confidence": float}.
    """
    if not text.strip():
        return {"tone": "neutral", "confidence": 0.5}

    if not config.live_sentiment_enabled:
        return {"tone": "neutral", "confidence": 0.5}

    if config.live_sentiment_use_openai:
        tone = classify_text(
            config,
            text,
            TONE_CATEGORIES,
            "Classify the emotional tone. Return only the label.",
            max_tokens=20,
            fallback_patterns=_TONE_PATTERNS,
        )
        confidence = 0.8 if config.openai_api_key else _fallback_confidence(text, tone)
        return {"tone": tone, "confidence": round(confidence, 2)}

    tone = _classify_live_segment_fallback(text)
    confidence = _fallback_confidence(text, tone)
    return {"tone": tone, "confidence": round(confidence, 2)}


def _classify_live_segment_fallback(text: str) -> str:
    text_lower = text.lower()
    scored = []
    for tone, patterns in _TONE_PATTERNS.items():
        if not patterns:
            continue
        hits = sum(1 for p in patterns if re.search(r"\b" + re.escape(p) + r"\b", text_lower))
        if hits > 0:
            scored.append((hits, tone))

    if not scored:
        return "neutral"

    scored.sort(key=lambda item: item[0], reverse=True)
    return scored[0][1]


def _fallback_confidence(text: str, tone: str) -> float:
    patterns = _TONE_PATTERNS.get(tone, [])
    if not patterns:
        return 0.3
    text_lower = text.lower()
    hits = sum(1 for p in patterns if re.search(r"\b" + re.escape(p) + r"\b", text_lower))
    return min(0.3 + hits * 0.15, 0.7)
