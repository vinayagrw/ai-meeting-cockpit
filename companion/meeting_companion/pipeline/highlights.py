"""Feature 4: Auto-highlight reel — extract the most important moments from a meeting."""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

from ..config import CompanionConfig
from ..models import FinalizedSession, TranscriptResult
from .openai_client import call_openai_json_safe

_IMPORTANCE_KEYWORDS = {
    "decision": ["decided", "decision", "agreed", "approved", "go with", "ship it", "confirmed", "resolved"],
    "action": ["action item", "will do", "i will", "we should", "next step", "assigned", "deadline", "follow up"],
    "disagreement": ["disagree", "push back", "concern", "worried", "not sure", "debate", "objection", "alternative"],
    "surprise": ["surprised", "unexpected", "interesting", "didn't know", "breaking", "news", "discovered", "found out"],
}


def extract_highlights(
    config: CompanionConfig,
    transcript: TranscriptResult,
    summary_markdown: str = "",
    agenda: dict | None = None,
) -> dict:
    """Extract 3-5 most important moments.

    Returns {"highlights": [{"title", "startSec", "endSec", "reason", "importance"}]}.
    """
    if not transcript.text.strip():
        return {"highlights": []}

    context_parts = [f"Transcript:\n{transcript.text[:config.automation_max_transcript_chars]}"]
    if summary_markdown:
        context_parts.append(f"\nSummary:\n{summary_markdown[:3000]}")
    if agenda and agenda.get("items"):
        agenda_text = "\n".join(f"- {item['topic']}" for item in agenda["items"])
        context_parts.append(f"\nAgenda topics:\n{agenda_text}")

    prompt = (
        "Identify the 3-5 most important moments in this meeting.\n"
        "Focus on: key decisions, action commitments, surprising information, disagreements, breakthroughs.\n"
        "For each, provide:\n"
        "- title: a short descriptive title (5-10 words)\n"
        "- startSec: approximate start time in seconds (use 0 if unknown)\n"
        "- endSec: approximate end time in seconds (use 0 if unknown)\n"
        "- reason: why this moment matters (one sentence)\n"
        "- importance: a score from 0.0 to 1.0\n\n"
        "Return strict JSON with a top-level key named highlights.\n\n"
        + "\n".join(context_parts)
    )

    fallback = _fallback_highlights(transcript)
    parsed = call_openai_json_safe(
        config,
        "Extract the most important meeting moments. Return JSON only.",
        prompt,
        config.highlights_openai_max_output_tokens,
        fallback={"highlights": fallback},
    )

    highlights = parsed.get("highlights", [])
    validated: list[dict] = []
    for item in highlights[:config.highlights_max_count]:
        if not isinstance(item, dict) or not item.get("title"):
            continue
        validated.append({
            "title": str(item["title"]),
            "startSec": float(item.get("startSec", 0)),
            "endSec": float(item.get("endSec", 0)),
            "reason": str(item.get("reason", "")),
            "importance": min(1.0, max(0.0, float(item.get("importance", 0.5)))),
        })

    validated.sort(key=lambda h: -h["importance"])
    return {"highlights": validated}


def export_highlight_clips(
    config: CompanionConfig,
    session: FinalizedSession,
    highlights_result: dict,
) -> dict:
    """Export MP4 clips for the strongest highlight moments when ffmpeg and recording.mp4 are available."""
    highlights = highlights_result.get("highlights", [])
    if not config.ffmpeg_path or not session.recording_path.exists() or session.recording_path.suffix.lower() != ".mp4":
        return {"clips": []}

    clips_dir = session.session_dir / "_session" / "highlight-clips"
    clips_dir.mkdir(parents=True, exist_ok=True)

    exported: list[dict] = []
    for index, highlight in enumerate(highlights, start=1):
        if not isinstance(highlight, dict):
            continue

        start_sec = float(highlight.get("startSec", 0) or 0)
        end_sec = float(highlight.get("endSec", 0) or 0)
        if end_sec <= start_sec:
            end_sec = start_sec + 12.0

        duration = max(4.0, min(end_sec - start_sec, 45.0))
        clip_name = f"highlight-{index:02d}-{_slugify_clip_title(str(highlight.get('title', 'moment')))}.mp4"
        clip_path = clips_dir / clip_name

        command = [
            config.ffmpeg_path,
            "-y",
            "-ss",
            f"{start_sec:.2f}",
            "-i",
            str(session.recording_path),
            "-t",
            f"{duration:.2f}",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-c:a",
            "aac",
            "-b:a",
            "128k",
            str(clip_path),
        ]

        try:
            subprocess.run(command, check=True, capture_output=True)
        except Exception:
            continue

        if not clip_path.exists() or clip_path.stat().st_size <= 0:
            continue

        exported.append({
            "title": str(highlight.get("title", "Moment")),
            "startSec": start_sec,
            "endSec": start_sec + duration,
            "path": str(clip_path),
            "fileName": clip_name,
        })

    return {"clips": exported}


def _fallback_highlights(transcript: TranscriptResult) -> list[dict]:
    """Score segments by keyword density to find important moments."""
    if not transcript.segments:
        return []

    scored: list[tuple[float, dict]] = []
    for segment in transcript.segments:
        text_lower = segment.text.lower()
        score = 0.0
        reasons: list[str] = []

        for category, keywords in _IMPORTANCE_KEYWORDS.items():
            for keyword in keywords:
                if re.search(r"\b" + re.escape(keyword) + r"\b", text_lower):
                    score += 1.0
                    if category not in reasons:
                        reasons.append(category)

        if score > 0:
            scored.append((score, {
                "title": segment.text[:60].strip(),
                "startSec": segment.start,
                "endSec": segment.end,
                "reason": f"Contains {', '.join(reasons)} keywords",
                "importance": min(1.0, score / 5.0),
            }))

    scored.sort(key=lambda x: -x[0])
    return [item for _, item in scored[:5]]


def _slugify_clip_title(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug[:48] or "moment"
