"""Feature 10: Topic timeline — track how a topic evolved across meetings."""

from __future__ import annotations

import json
import logging
from collections import defaultdict
from pathlib import Path

from ..config import CompanionConfig
from .openai_client import call_openai_safe

logger = logging.getLogger(__name__)


def build_topic_timeline(
    config: CompanionConfig,
    topic_query: str,
    limit: int = 10,
) -> dict:
    """Track a topic across all meetings chronologically.

    Returns {"topic", "timeline": [{"sessionDir", "date", "title", "excerpt", "decision"}]}.
    """
    if not topic_query.strip():
        return {"topic": topic_query, "timeline": []}

    matches = _find_topic_mentions(config, topic_query.strip())
    if not matches:
        return {"topic": topic_query, "timeline": []}

    timeline: list[dict] = []
    for match in matches[:limit]:
        decision = None
        if config.openai_api_key and match["excerpt"]:
            decision = call_openai_safe(
                config,
                "Extract the key decision or state change about the topic. Return one sentence or 'No decision'.",
                (
                    f"Topic: {topic_query}\n"
                    f"Meeting: {match['title']}\n"
                    f"Excerpt: {match['excerpt'][:1000]}\n\n"
                    "What was the key decision or state change regarding this topic? One sentence."
                ),
                config.timeline_openai_max_output_tokens,
            )
            if decision and decision.lower().startswith("no decision"):
                decision = None

        timeline.append({
            "sessionDir": match["sessionDir"],
            "date": match["date"],
            "title": match["title"],
            "excerpt": match["excerpt"][:300],
            "decision": decision,
        })

    timeline.sort(key=lambda t: t["date"])
    return {"topic": topic_query, "timeline": timeline}


def _find_topic_mentions(config: CompanionConfig, query: str) -> list[dict]:
    """Find sessions mentioning the topic using keyword search."""
    query_lower = query.lower()
    query_terms = query_lower.split()
    matches: list[dict] = []

    if not config.meetings_root.exists():
        return []

    for session_dir in sorted(config.meetings_root.glob("*/*"), reverse=True):
        metadata_path = session_dir / "metadata.json"
        if not metadata_path.exists():
            continue

        try:
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        except (OSError, ValueError, json.JSONDecodeError):
            continue

        transcript_text = _read_file(session_dir / "transcript.txt")
        summary_text = _read_file(session_dir / "summary.md")
        haystack = f"{transcript_text}\n{summary_text}".lower()

        if not any(term in haystack for term in query_terms):
            continue

        excerpt = _extract_relevant_excerpt(transcript_text, query_terms)
        if not excerpt:
            excerpt = _extract_relevant_excerpt(summary_text, query_terms)

        matches.append({
            "sessionDir": str(session_dir),
            "date": str(metadata.get("startedAt", "")),
            "title": metadata.get("title", session_dir.name),
            "excerpt": excerpt,
        })

    return matches


def _extract_relevant_excerpt(text: str, query_terms: list[str]) -> str:
    """Find the most relevant paragraph containing query terms."""
    lines = text.splitlines()
    best_score = 0
    best_start = 0

    for i, line in enumerate(lines):
        line_lower = line.lower()
        score = sum(1 for term in query_terms if term in line_lower)
        if score > best_score:
            best_score = score
            best_start = i

    if best_score == 0:
        return ""

    start = max(0, best_start - 1)
    end = min(len(lines), best_start + 4)
    return "\n".join(lines[start:end]).strip()[:500]


def _read_file(path: Path) -> str:
    if not path.exists():
        return ""
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return ""
