from __future__ import annotations

from datetime import datetime
import json
import re
from urllib import error, request

from ..config import CompanionConfig
from ..models import SessionStartRequest, TranscriptResult


ACTION_SENTENCE_PATTERN = re.compile(
    r"\b(?:action item|todo|follow up|follow-up|i will|we should|please|need to|next step)\b",
    re.IGNORECASE,
)
OWNER_PATTERN = re.compile(r"\b(i|we|[A-Z][a-z]+)\b")
DATE_PATTERN = re.compile(r"\b(?:today|tomorrow|next week|monday|tuesday|wednesday|thursday|friday|saturday|sunday|\d{1,2}/\d{1,2})\b", re.IGNORECASE)


def _extract_relevant_sentences(transcript_text: str) -> list[str]:
    lines = [line.split("] ", 1)[-1].strip() for line in transcript_text.splitlines() if line.strip()]
    sentences: list[str] = []
    for line in lines:
        sentences.extend([part.strip() for part in re.split(r"(?<=[.!?])\s+", line) if part.strip()])
    return sentences


def build_fallback_action_items(transcript: TranscriptResult) -> list[dict]:
    items: list[dict] = []
    for sentence in _extract_relevant_sentences(transcript.text):
        if not ACTION_SENTENCE_PATTERN.search(sentence):
            continue
        owner_match = OWNER_PATTERN.search(sentence)
        due_match = DATE_PATTERN.search(sentence)
        items.append(
            {
                "task": sentence,
                "owner": owner_match.group(1) if owner_match else None,
                "dueDate": due_match.group(0) if due_match else None,
                "source": "transcript-fallback",
            }
        )
    return items


def build_follow_up_markdown(meeting: SessionStartRequest, summary_markdown: str, action_items: list[dict]) -> str:
    lines = [
        f"# Follow-up for {meeting.title}",
        "",
        f"- Platform: {meeting.platform}",
        f"- Meeting ID: {meeting.meeting_id}",
        f"- Generated: {datetime.now().isoformat(timespec='seconds')}",
        "",
        "## Summary",
        summary_markdown.strip() or "No summary available.",
        "",
        "## Suggested Follow-up",
        "Hi team,",
        "",
        "Here is a quick recap of the meeting and the next steps.",
        "",
        "## Action Items",
    ]
    if action_items:
        for item in action_items:
            owner = f" ({item['owner']})" if item.get("owner") else ""
            due = f" due {item['dueDate']}" if item.get("dueDate") else ""
            lines.append(f"- {item['task']}{owner}{due}")
    else:
        lines.append("- No explicit action items were detected.")

    lines.extend(["", "Thanks,"])
    return "\n".join(lines)


def build_action_items_with_openai(
    config: CompanionConfig,
    meeting: SessionStartRequest,
    transcript: TranscriptResult,
) -> list[dict]:
    if not config.openai_api_key:
        return build_fallback_action_items(transcript)[:config.automation_max_action_items]

    prompt = (
        "Extract action items from this meeting transcript.\n"
        "Write all task text in English only.\n"
        "Return strict JSON with a top-level key named items.\n"
        "Each item must have task, owner, dueDate, and source.\n"
        "Use null when owner or dueDate is unknown.\n\n"
        f"Meeting title: {meeting.title}\n"
        f"Platform: {meeting.platform}\n\n"
        f"Transcript:\n{transcript.text[:config.automation_max_transcript_chars]}"
    )
    payload = json.dumps(
        {
            "model": config.openai_model,
            "instructions": "Return JSON only. Use English only.",
            "input": prompt,
            "max_output_tokens": config.automation_openai_max_output_tokens,
        }
    ).encode("utf-8")
    http_request = request.Request(
        f"{config.openai_base_url.rstrip('/')}/responses",
        data=payload,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {config.openai_api_key}",
        },
        method="POST",
    )

    try:
        with request.urlopen(http_request, timeout=config.automation_openai_timeout_seconds) as response:
            body = json.loads(response.read().decode("utf-8"))
        text = str(body.get("output_text", "")).strip()
        if not text:
            for output_item in body.get("output", []) or []:
                for content_item in output_item.get("content", []) or []:
                    if content_item.get("type") == "output_text":
                        text = str(content_item.get("text", "")).strip()
                        if text:
                            break
                if text:
                    break
        parsed = json.loads(text)
        items = parsed.get("items", [])
        return [item for item in items if isinstance(item, dict)][:config.automation_max_action_items]
    except (error.URLError, error.HTTPError, TimeoutError, OSError, ValueError, json.JSONDecodeError):
        return build_fallback_action_items(transcript)[:config.automation_max_action_items]
