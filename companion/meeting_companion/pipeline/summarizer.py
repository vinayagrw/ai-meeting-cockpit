from __future__ import annotations

from datetime import datetime
import json
import re
from typing import Protocol
from urllib import error, request

from ..config import CompanionConfig
from ..models import SessionStartRequest, SummaryResult, TranscriptResult
from .openai_client import call_openai


ACTION_PATTERN = re.compile(r"\b(?:action item|follow up|follow-up|todo|i will|we should|next step)\b", re.IGNORECASE)
DECISION_PATTERN = re.compile(r"\b(?:decided|decision|agreed|approved|ship it|go with)\b", re.IGNORECASE)
RISK_PATTERN = re.compile(r"\b(?:risk|blocker|blocked|concern|issue|delay|dependency|waiting on|problem)\b", re.IGNORECASE)
QUESTION_PATTERN = re.compile(r"\b(?:question|unclear|need to confirm|follow up on|to confirm|not sure|open item)\b", re.IGNORECASE)


def _extract_sentences(text: str) -> list[str]:
    cleaned = [line.split("] ", 1)[-1].strip() for line in text.splitlines() if line.strip()]
    collapsed = " ".join(cleaned)
    return [sentence.strip() for sentence in re.split(r"(?<=[.!?])\s+", collapsed) if sentence.strip()]


def _format_timestamp(value: float) -> str:
    total_seconds = max(int(value), 0)
    hours = total_seconds // 3600
    minutes = (total_seconds % 3600) // 60
    seconds = total_seconds % 60
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}"


def _transcript_entries(transcript: TranscriptResult) -> list[dict[str, str | float | None]]:
    if transcript.segments:
        entries: list[dict[str, str | float | None]] = []
        for segment in transcript.segments:
            text = " ".join(segment.text.split()).strip()
            if not text:
                continue
            entries.append(
                {
                    "start": segment.start,
                    "end": segment.end,
                    "speaker": segment.speaker,
                    "text": text,
                }
            )
        if entries:
            return entries

    entries = []
    for index, line in enumerate(transcript.text.splitlines()):
        text = line.strip()
        if not text:
            continue
        if "] " in text:
            text = text.split("] ", 1)[-1].strip()
        speaker = None
        if ": " in text:
            possible_speaker, possible_text = text.split(": ", 1)
            if 0 < len(possible_speaker) <= 40:
                speaker = possible_speaker.strip()
                text = possible_text.strip()
        entries.append(
            {
                "start": float(index * 5),
                "end": float(index * 5 + 4),
                "speaker": speaker,
                "text": text,
            }
        )
    return entries


def _sample_entries(entries: list[dict[str, str | float | None]], limit: int) -> list[dict[str, str | float | None]]:
    if len(entries) <= limit:
        return entries

    sampled: list[dict[str, str | float | None]] = []
    last_index = len(entries) - 1
    for position in range(limit):
        index = round(position * last_index / max(limit - 1, 1))
        sampled.append(entries[index])
    return sampled


def _format_entry(entry: dict[str, str | float | None]) -> str:
    speaker = str(entry.get("speaker") or "").strip()
    text = str(entry.get("text") or "").strip()
    start = _format_timestamp(float(entry.get("start") or 0.0))
    end = _format_timestamp(float(entry.get("end") or 0.0))
    if speaker:
        return f"[{start} - {end}] {speaker}: {text}"
    return f"[{start} - {end}] {text}"


def _build_evidence_blocks(transcript: TranscriptResult) -> dict[str, list[str]]:
    entries = _transcript_entries(transcript)
    participants = sorted(
        {
            str(entry.get("speaker")).strip()
            for entry in entries
            if str(entry.get("speaker") or "").strip()
        },
        key=str.casefold,
    )

    decisions = [_format_entry(entry) for entry in entries if DECISION_PATTERN.search(str(entry.get("text") or ""))]
    action_items = [_format_entry(entry) for entry in entries if ACTION_PATTERN.search(str(entry.get("text") or ""))]
    risks = [_format_entry(entry) for entry in entries if RISK_PATTERN.search(str(entry.get("text") or ""))]
    open_questions = [_format_entry(entry) for entry in entries if QUESTION_PATTERN.search(str(entry.get("text") or ""))]
    highlights = [_format_entry(entry) for entry in _sample_entries(entries, 14)]

    return {
        "participants": participants[:12],
        "highlights": highlights[:14],
        "decisions": decisions[:8],
        "action_items": action_items[:8],
        "risks": risks[:6],
        "open_questions": open_questions[:6],
    }


def _render_block(title: str, lines: list[str], empty_text: str) -> str:
    content = lines or [empty_text]
    return f"{title}:\n" + "\n".join(f"- {line}" for line in content)


def _build_summary_prompt(meeting: SessionStartRequest, transcript: TranscriptResult, max_transcript_chars: int) -> str:
    evidence = _build_evidence_blocks(transcript)
    transcript_excerpt = transcript.text[:max_transcript_chars].strip()
    return (
        "Summarize this meeting transcript in Markdown.\n"
        "Write everything in English only.\n"
        "Use plain, straightforward wording.\n"
        "Be faithful to the evidence and do not invent facts.\n"
        "Return Markdown only.\n"
        "Use these sections exactly in this order: Overview, Key Points, Decisions, Action Items, Risks, Open Questions.\n"
        "Each section after Overview should use bullet points.\n"
        "For Action Items, include the owner only if it is explicit in the transcript.\n"
        "If a section is not supported by the transcript, say that explicitly instead of guessing.\n\n"
        f"Platform: {meeting.platform}\n"
        f"Meeting title: {meeting.title}\n"
        f"Meeting ID: {meeting.meeting_id}\n"
        f"Started at: {meeting.started_at}\n"
        f"Transcript status: {transcript.status}\n\n"
        + _render_block("Detected participants", evidence["participants"], "No named participants were detected.")
        + "\n\n"
        + _render_block("Representative transcript excerpts", evidence["highlights"], "No representative excerpts were available.")
        + "\n\n"
        + _render_block("Decision candidates", evidence["decisions"], "No explicit decisions were detected.")
        + "\n\n"
        + _render_block("Action item candidates", evidence["action_items"], "No explicit action items were detected.")
        + "\n\n"
        + _render_block("Risk and blocker candidates", evidence["risks"], "No explicit risks or blockers were detected.")
        + "\n\n"
        + _render_block("Open question candidates", evidence["open_questions"], "No explicit open questions were detected.")
        + "\n\n"
        + "Transcript excerpt:\n"
        + transcript_excerpt
    )


def build_fallback_summary(meeting: SessionStartRequest, transcript: TranscriptResult, *, max_highlights: int = 5, max_decisions: int = 5, max_action_items: int = 5) -> str:
    evidence = _build_evidence_blocks(transcript)
    highlights = evidence["highlights"][:max_highlights] or ["No detailed transcript content was available."]
    action_items = evidence["action_items"][:max_action_items]
    decisions = evidence["decisions"][:max_decisions]
    risks = evidence["risks"][:max_highlights]
    open_questions = evidence["open_questions"][:max_highlights]

    lines = [
        f"# {meeting.title}",
        "",
        "## Overview",
        f"- Platform: {meeting.platform}",
        f"- Meeting ID: {meeting.meeting_id}",
        f"- Started: {meeting.started_at}",
        f"- Summary generated: {datetime.now().isoformat(timespec='seconds')}",
    ]
    if evidence["participants"]:
        lines.append(f"- Participants: {', '.join(evidence['participants'])}")
    lines.extend(
        [
        "",
        "## Key Points",
        ]
    )
    lines.extend(f"- {line}" for line in highlights)
    lines.append("")
    lines.append("## Decisions")
    lines.extend(f"- {line}" for line in decisions or ["No explicit decisions were detected."])
    lines.append("")
    lines.append("## Action Items")
    lines.extend(f"- {line}" for line in action_items or ["No explicit action items were detected."])
    lines.append("")
    lines.append("## Risks")
    lines.extend(f"- {line}" for line in risks or ["No explicit risks were detected from the available transcript."])
    lines.append("")
    lines.append("## Open Questions")
    lines.extend(f"- {line}" for line in open_questions or ["No explicit open questions were detected."])
    lines.append("")
    lines.append("## Notes")
    lines.append(f"- Transcript status: {transcript.status}")
    if transcript.error:
        lines.append(f"- Transcript error: {transcript.error}")
    return "\n".join(lines)


class Summarizer(Protocol):
    def summarize(self, meeting: SessionStartRequest, transcript: TranscriptResult) -> SummaryResult:
        ...


class FallbackSummarizer:
    def __init__(self, config: CompanionConfig | None = None) -> None:
        self.config = config

    def summarize(self, meeting: SessionStartRequest, transcript: TranscriptResult) -> SummaryResult:
        markdown = build_fallback_summary(
            meeting,
            transcript,
            max_highlights=self.config.summary_max_highlights if self.config else 5,
            max_decisions=self.config.summary_max_decisions if self.config else 5,
            max_action_items=self.config.summary_max_action_items if self.config else 5,
        )
        return SummaryResult(status="fallback", markdown=markdown, provider="fallback")


class OllamaSummarizer:
    def __init__(self, config: CompanionConfig) -> None:
        self.config = config

    def _call_ollama(self, prompt: str) -> str:
        payload = json.dumps(
            {
                "model": self.config.ollama_model,
                "prompt": prompt,
                "stream": False,
            }
        ).encode("utf-8")
        http_request = request.Request(
            f"{self.config.ollama_url}/api/generate",
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with request.urlopen(http_request, timeout=self.config.ollama_timeout_seconds) as response:
            body = json.loads(response.read().decode("utf-8"))
        return str(body.get("response", "")).strip()

    def summarize(self, meeting: SessionStartRequest, transcript: TranscriptResult) -> SummaryResult:
        prompt = _build_summary_prompt(meeting, transcript, self.config.summary_max_transcript_chars)
        try:
            response = self._call_ollama(prompt)
            if response:
                return SummaryResult(status="completed", markdown=response, provider="ollama")
        except (error.URLError, TimeoutError, OSError, ValueError) as call_error:
            return SummaryResult(
                status="fallback",
                markdown=build_fallback_summary(
                    meeting,
                    transcript,
                    max_highlights=self.config.summary_max_highlights,
                    max_decisions=self.config.summary_max_decisions,
                    max_action_items=self.config.summary_max_action_items,
                ),
                provider="fallback",
                error=str(call_error),
            )

        return SummaryResult(
            status="fallback",
            markdown=build_fallback_summary(
                meeting,
                transcript,
                max_highlights=self.config.summary_max_highlights,
                max_decisions=self.config.summary_max_decisions,
                max_action_items=self.config.summary_max_action_items,
            ),
            provider="fallback",
            error="Ollama returned an empty response",
        )


class OpenAISummarizer:
    def __init__(self, config: CompanionConfig) -> None:
        self.config = config

    def summarize(self, meeting: SessionStartRequest, transcript: TranscriptResult) -> SummaryResult:
        prompt = _build_summary_prompt(meeting, transcript, self.config.summary_max_transcript_chars)
        try:
            response = call_openai(
                self.config,
                "You are a concise meeting summarizer. Return Markdown only. Write in English only.",
                prompt,
                self.config.openai_summary_max_output_tokens,
            )
            return SummaryResult(status="completed", markdown=response, provider="openai")
        except (error.URLError, error.HTTPError, TimeoutError, OSError, ValueError, json.JSONDecodeError) as call_error:
            return SummaryResult(
                status="fallback",
                markdown=build_fallback_summary(
                    meeting,
                    transcript,
                    max_highlights=self.config.summary_max_highlights,
                    max_decisions=self.config.summary_max_decisions,
                    max_action_items=self.config.summary_max_action_items,
                ),
                provider="fallback",
                error=str(call_error),
            )


def build_summarizer(config: CompanionConfig) -> Summarizer:
    provider = config.summary_provider.lower().strip()
    if provider == "openai":
        if config.openai_api_key:
            return OpenAISummarizer(config)
        return FallbackSummarizer(config)
    if provider == "fallback":
        return FallbackSummarizer(config)
    return OllamaSummarizer(config)
