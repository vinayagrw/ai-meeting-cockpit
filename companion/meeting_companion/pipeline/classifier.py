"""Shared text classification utility — used by sentiment, meeting type, and agenda features."""

from __future__ import annotations

import logging
import re

from ..config import CompanionConfig
from .openai_client import call_openai_safe

logger = logging.getLogger(__name__)


def classify_text(
    config: CompanionConfig,
    text: str,
    categories: list[str],
    instructions: str,
    *,
    max_tokens: int = 50,
    fallback_patterns: dict[str, list[str]] | None = None,
) -> str:
    """Classify text into one of the given categories.

    Uses OpenAI as primary, falls back to keyword pattern matching.
    Returns one of the category labels.
    """
    if not text.strip() or not categories:
        return categories[0] if categories else ""

    if config.openai_api_key:
        prompt = (
            f"Classify the following text into exactly one of these categories: {', '.join(categories)}.\n"
            f"Return only the category label, nothing else.\n\n"
            f"Text:\n{text[:4000]}"
        )
        result = call_openai_safe(config, instructions, prompt, max_tokens)
        result_lower = result.strip().lower()
        for category in categories:
            if category.lower() in result_lower:
                return category
        if result.strip():
            logger.debug("OpenAI returned unrecognized category: %s", result.strip())

    return _fallback_classify(text, categories, fallback_patterns)


def classify_segments(
    config: CompanionConfig,
    segments: list[dict],
    categories: list[str],
    instructions: str,
    *,
    max_tokens: int = 500,
    fallback_patterns: dict[str, list[str]] | None = None,
) -> list[dict]:
    """Classify multiple transcript segments in a single OpenAI call.

    Returns list of {"start_sec", "end_sec", "text", "label", "confidence"}.
    """
    if not segments or not categories:
        return []

    if config.openai_api_key and len(segments) <= 50:
        numbered = "\n".join(f"{i+1}. {seg.get('text', '')[:200]}" for i, seg in enumerate(segments[:50]))
        prompt = (
            f"Classify each numbered line into exactly one of: {', '.join(categories)}.\n"
            f"Return one label per line in format: NUMBER. LABEL\n\n"
            f"{numbered}"
        )
        result = call_openai_safe(config, instructions, prompt, max_tokens)
        labels = _parse_numbered_labels(result, categories, len(segments))
        if labels:
            return [
                {
                    "start_sec": seg.get("start", 0.0),
                    "end_sec": seg.get("end", 0.0),
                    "text": seg.get("text", ""),
                    "label": label,
                    "confidence": 0.8 if label != categories[0] else 0.5,
                }
                for seg, label in zip(segments, labels)
            ]

    return [
        {
            "start_sec": seg.get("start", 0.0),
            "end_sec": seg.get("end", 0.0),
            "text": seg.get("text", ""),
            "label": _fallback_classify(seg.get("text", ""), categories, fallback_patterns),
            "confidence": 0.3,
        }
        for seg in segments
    ]


def _fallback_classify(text: str, categories: list[str], patterns: dict[str, list[str]] | None) -> str:
    """Keyword-based classification fallback."""
    if not patterns:
        return categories[0] if categories else ""

    text_lower = text.lower()
    scores: dict[str, int] = {cat: 0 for cat in categories}

    for category, keywords in patterns.items():
        if category in scores:
            for keyword in keywords:
                if re.search(r"\b" + re.escape(keyword.lower()) + r"\b", text_lower):
                    scores[category] += 1

    best = max(scores, key=lambda k: scores[k])
    return best if scores[best] > 0 else categories[0]


def _parse_numbered_labels(result: str, categories: list[str], expected_count: int) -> list[str]:
    """Parse '1. label\\n2. label' format from OpenAI response."""
    cat_lower = {c.lower(): c for c in categories}
    labels: list[str] = []

    for line in result.strip().splitlines():
        line = line.strip()
        match = re.match(r"\d+\.\s*(.+)", line)
        if match:
            raw = match.group(1).strip().lower()
            matched = cat_lower.get(raw)
            if matched:
                labels.append(matched)
            else:
                for cat_key, cat_val in cat_lower.items():
                    if cat_key in raw:
                        labels.append(cat_val)
                        break
                else:
                    labels.append(categories[0])

    if len(labels) < expected_count:
        labels.extend([categories[0]] * (expected_count - len(labels)))

    return labels[:expected_count]
