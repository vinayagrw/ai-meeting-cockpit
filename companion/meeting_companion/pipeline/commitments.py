"""Feature 7: Commitment tracker — extract and track promises across meetings."""

from __future__ import annotations

import json
import logging
import re
from pathlib import Path

from ..config import CompanionConfig
from ..models import SessionStartRequest, TranscriptResult
from .openai_client import call_openai_json_safe

logger = logging.getLogger(__name__)

_COMMITMENT_PATTERN = re.compile(
    r"\b(?:i will|i'll|we will|we'll|we agreed|committed to|promise|assigned to|"
    r"take on|own this|my action|your action|responsible for)\b",
    re.IGNORECASE,
)


def extract_commitments(
    config: CompanionConfig,
    transcript: TranscriptResult,
    meeting: SessionStartRequest,
) -> list[dict]:
    """Extract commitments from a single session.

    Returns [{"commitment", "owner", "dueDate", "meetingDate", "sessionDir", "status"}].
    """
    if not transcript.text.strip():
        return []

    prompt = (
        "Extract explicit commitments from this meeting transcript.\n"
        "A commitment is when someone promises to do something specific.\n"
        "For each, provide:\n"
        "- commitment: what was promised (one sentence)\n"
        "- owner: who made the commitment (name or role)\n"
        "- dueDate: when it's due (null if unknown)\n\n"
        "Return strict JSON with a top-level key named items.\n"
        "Only include clear commitments, not vague suggestions.\n\n"
        f"Meeting title: {meeting.title}\n"
        f"Platform: {meeting.platform}\n"
        f"Date: {meeting.started_at}\n\n"
        f"Transcript:\n{transcript.text[:config.automation_max_transcript_chars]}"
    )

    fallback = _fallback_commitments(transcript)
    parsed = call_openai_json_safe(
        config,
        "Extract commitments only. Return JSON only. Use English only.",
        prompt,
        config.commitments_openai_max_output_tokens,
        fallback={"items": fallback},
    )

    items = parsed.get("items", [])
    return [
        {
            "commitment": str(item.get("commitment", "")),
            "owner": item.get("owner"),
            "dueDate": item.get("dueDate"),
            "meetingDate": meeting.started_at,
            "status": "open",
        }
        for item in items[:config.commitments_max_items]
        if isinstance(item, dict) and item.get("commitment")
    ]


def get_open_commitments(config: CompanionConfig, limit: int = 20) -> list[dict]:
    """Scan all sessions for open commitments. Returns sorted by date."""
    all_commitments: list[dict] = []

    if not config.meetings_root.exists():
        return []

    for session_dir in sorted(config.meetings_root.glob("*/*"), reverse=True):
        try:
            payload_items: list[dict] = []
            insights_path = session_dir / "insights.json"
            commitments_path = session_dir / "commitments.json"

            if insights_path.exists():
                insights_payload = json.loads(insights_path.read_text(encoding="utf-8"))
                commitments_payload = insights_payload.get("commitments", {})
                if isinstance(commitments_payload, dict):
                    payload_items = list(commitments_payload.get("items", []) or [])
            elif commitments_path.exists():
                legacy_payload = json.loads(commitments_path.read_text(encoding="utf-8"))
                payload_items = list(legacy_payload.get("items", []) or [])

            for item in payload_items:
                if isinstance(item, dict) and item.get("status") == "open":
                    item["sessionDir"] = str(session_dir)
                    all_commitments.append(item)
        except (OSError, ValueError, json.JSONDecodeError):
            continue

        if len(all_commitments) >= limit * 2:
            break

    all_commitments.sort(key=lambda c: c.get("meetingDate", ""), reverse=True)
    return all_commitments[:limit]


def _fallback_commitments(transcript: TranscriptResult) -> list[dict]:
    """Keyword-based commitment extraction."""
    items: list[dict] = []
    for line in transcript.text.splitlines():
        text = line.strip()
        if not text:
            continue
        cleaned = text.split("] ", 1)[-1] if "] " in text else text
        if _COMMITMENT_PATTERN.search(cleaned):
            speaker = None
            if ": " in cleaned:
                parts = cleaned.split(": ", 1)
                if len(parts[0]) <= 40:
                    speaker = parts[0].strip()
                    cleaned = parts[1].strip()
            items.append({
                "commitment": cleaned[:200],
                "owner": speaker,
                "dueDate": None,
            })
    return items[:10]
