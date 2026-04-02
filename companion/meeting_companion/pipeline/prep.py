"""Feature 9: Meeting prep brief — summarize prior related meetings before a new one."""

from __future__ import annotations

import json
import logging
from pathlib import Path

from ..config import CompanionConfig
from .openai_client import call_openai_safe

logger = logging.getLogger(__name__)


def generate_prep_brief(
    config: CompanionConfig,
    meeting_title: str,
    participants: list[str] | None = None,
) -> str:
    """Search past meetings with similar title/participants, generate a prep brief."""
    related = _find_related_sessions(config, meeting_title, participants)
    if not related:
        return f"No previous meetings found related to '{meeting_title}'."

    context_parts: list[str] = []
    for session in related[:config.prep_max_past_sessions]:
        session_dir = Path(session["sessionDir"])
        summary = _read_file(session_dir / "summary.md")
        action_items = _read_file(session_dir / "action-items.json")
        commitments = _read_file(session_dir / "commitments.json")

        parts = [f"### {session['title']} ({session.get('startedAt', 'unknown date')})"]
        if summary:
            parts.append(f"**Summary:**\n{summary[:2000]}")
        if action_items:
            parts.append(f"**Action Items:**\n{action_items[:1000]}")
        if commitments:
            parts.append(f"**Commitments:**\n{commitments[:1000]}")
        context_parts.append("\n".join(parts))

    combined_context = "\n\n---\n\n".join(context_parts)

    brief = call_openai_safe(
        config,
        "Generate a concise meeting prep brief. Write in English only.",
        (
            "Generate a meeting preparation brief based on these prior related meetings.\n"
            "Include:\n"
            "1. Key decisions made previously\n"
            "2. Open action items and commitments still pending\n"
            "3. Unresolved questions or topics to follow up on\n"
            "4. Suggested talking points for the upcoming meeting\n\n"
            f"Upcoming meeting: {meeting_title}\n"
            + (f"Expected participants: {', '.join(participants)}\n" if participants else "")
            + f"\nPrior meetings:\n{combined_context}"
        ),
        config.prep_openai_max_output_tokens,
    )

    if brief:
        return brief

    return _fallback_prep(meeting_title, related)


def _find_related_sessions(
    config: CompanionConfig,
    title: str,
    participants: list[str] | None,
) -> list[dict]:
    """Find sessions related by title similarity."""
    title_lower = title.lower()
    title_words = set(title_lower.split())
    results: list[tuple[int, dict]] = []

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

        session_title = str(metadata.get("title", "")).lower()
        session_words = set(session_title.split())
        overlap = len(title_words & session_words)

        if overlap >= 2 or title_lower in session_title or session_title in title_lower:
            results.append((overlap, {
                "sessionDir": str(session_dir),
                "title": metadata.get("title", session_dir.name),
                "platform": metadata.get("platform"),
                "startedAt": metadata.get("startedAt"),
            }))

    results.sort(key=lambda r: -r[0])
    return [r[1] for r in results[:config.prep_max_past_sessions]]


def _fallback_prep(title: str, related: list[dict]) -> str:
    """Simple concatenation of prior summaries."""
    lines = [f"# Prep Brief for: {title}", "", "## Prior Related Meetings", ""]
    for session in related:
        session_dir = Path(session["sessionDir"])
        summary = _read_file(session_dir / "summary.md")
        lines.append(f"### {session['title']} ({session.get('startedAt', '')})")
        if summary:
            lines.append(summary[:1500])
        else:
            lines.append("No summary available.")
        lines.append("")
    return "\n".join(lines)


def _read_file(path: Path) -> str:
    if not path.exists():
        return ""
    try:
        return path.read_text(encoding="utf-8").strip()
    except OSError:
        return ""
