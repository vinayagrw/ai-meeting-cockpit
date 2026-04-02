from __future__ import annotations

import json
from pathlib import Path
import re

from .config import CompanionConfig
from .pipeline.openai_client import call_openai_safe
from .storage import SessionStorage, resolve_session_file

_STOP_WORDS = {
    "a",
    "about",
    "an",
    "and",
    "are",
    "at",
    "be",
    "did",
    "do",
    "does",
    "for",
    "from",
    "how",
    "i",
    "in",
    "is",
    "it",
    "of",
    "on",
    "or",
    "that",
    "the",
    "this",
    "to",
    "was",
    "we",
    "were",
    "what",
    "when",
    "where",
    "who",
    "why",
    "will",
    "with",
}


def search_meetings(storage: SessionStorage, query: str, limit: int = 10) -> list[dict]:
    needle = query.strip().lower()
    results: list[dict] = []
    if not needle:
        return results

    for session_dir in sorted(storage.meetings_root.glob("*/*"), reverse=True):
        metadata_path = session_dir / "metadata.json"
        if not metadata_path.exists():
            continue

        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        transcript_text = (session_dir / "transcript.txt").read_text(encoding="utf-8") if (session_dir / "transcript.txt").exists() else ""
        summary_text = (session_dir / "summary.md").read_text(encoding="utf-8") if (session_dir / "summary.md").exists() else ""
        follow_up_text = (session_dir / "follow-up.md").read_text(encoding="utf-8") if (session_dir / "follow-up.md").exists() else ""
        action_items_text = (session_dir / "action-items.json").read_text(encoding="utf-8") if (session_dir / "action-items.json").exists() else ""
        haystack = "\n".join(
            [
                str(metadata.get("title", "")),
                str(metadata.get("platform", "")),
                transcript_text,
                summary_text,
                follow_up_text,
                action_items_text,
            ]
        ).lower()

        if needle not in haystack:
            continue

        snippet_source = transcript_text or summary_text or str(metadata.get("title", ""))
        index = snippet_source.lower().find(needle)
        start = max(0, index - 80) if index >= 0 else 0
        end = min(len(snippet_source), start + 220)
        results.append(
            {
                "sessionDir": str(session_dir),
                "title": metadata.get("title", session_dir.name),
                "platform": metadata.get("platform"),
                "startedAt": metadata.get("startedAt"),
                "snippet": snippet_source[start:end].replace("\n", " ").strip(),
            }
        )
        if len(results) >= limit:
            break

    return results


def ask_meeting_question(config: CompanionConfig, session_dir: Path, question: str) -> str:
    summary_path = session_dir / "summary.md"
    transcript_path = session_dir / "transcript.txt"
    metadata_path = session_dir / "metadata.json"

    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.exists() else {}
    summary_text = summary_path.read_text(encoding="utf-8") if summary_path.exists() else ""
    transcript_text = transcript_path.read_text(encoding="utf-8") if transcript_path.exists() else ""
    transcript_segments = _load_transcript_segments(session_dir)
    participants = _load_participants(session_dir, metadata)
    relevant_excerpts = _select_relevant_excerpts(question, transcript_segments, limit=config.history_max_relevant_excerpts)

    if config.openai_api_key:
        excerpt_block = "\n".join(_format_excerpt(excerpt) for excerpt in relevant_excerpts) or "No strongly matching transcript excerpts were found."
        participant_block = ", ".join(participants) if participants else "No named participants were detected."
        prompt = (
            "Answer the user's question about a meeting using the provided context.\n"
            "Prioritize the transcript excerpts over the summary.\n"
            "Quote timestamps and speaker names when they are available.\n"
            "If the transcript does not support the answer, say so clearly instead of guessing.\n"
            "Write the answer in English only.\n\n"
            f"Meeting title: {metadata.get('title', session_dir.name)}\n"
            f"Platform: {metadata.get('platform', 'unknown')}\n\n"
            f"Detected participants: {participant_block}\n\n"
            f"Relevant transcript excerpts:\n{excerpt_block}\n\n"
            f"Transcript:\n{transcript_text[:config.history_max_transcript_chars]}\n\n"
            f"Summary:\n{summary_text[:config.history_max_summary_chars]}\n\n"
            f"Question: {question}"
        )
        answer = call_openai_safe(
            config,
            "Answer the meeting question using only the provided context. "
            "Prefer transcript evidence, cite timestamps when possible, and write in English only.",
            prompt,
            config.history_openai_max_output_tokens,
            timeout=config.history_openai_timeout_seconds,
        )
        if answer:
            return answer

    if relevant_excerpts:
        lines = [
            "I could not run the full meeting Q&A model, so here are the most relevant transcript excerpts:",
            *[f"- {_format_excerpt(excerpt)}" for excerpt in relevant_excerpts[:config.history_fallback_excerpt_count]],
        ]
        if participants:
            lines.append("")
            lines.append(f"Detected participants: {', '.join(participants)}")
        return "\n".join(lines)

    if summary_text.strip():
        return (
            "I could not find a strong transcript match for that question. "
            "Here is the saved meeting summary instead:\n\n"
            f"{summary_text.strip()}"
        )

    return "No matching answer was found in the saved meeting context."


def _load_participants(session_dir: Path, metadata: dict) -> list[str]:
    participants: list[str] = []
    participants_path = resolve_session_file(session_dir, ["participants.json"])
    if participants_path.exists():
        try:
            payload = json.loads(participants_path.read_text(encoding="utf-8"))
            raw_participants = payload.get("participants", [])
            if isinstance(raw_participants, list):
                participants.extend(str(item).strip() for item in raw_participants if str(item).strip())
        except (OSError, ValueError, json.JSONDecodeError):
            pass

    hint = str(metadata.get("participantsHint", "")).strip()
    if hint:
        participants.extend(part.strip() for part in re.split(r"[,\n;|]+", hint) if part.strip())

    seen: set[str] = set()
    ordered: list[str] = []
    for participant in participants:
        key = participant.casefold()
        if key in seen:
            continue
        seen.add(key)
        ordered.append(participant)
    return ordered


def _load_transcript_segments(session_dir: Path) -> list[dict]:
    candidates = [
        resolve_session_file(session_dir, ["speaker-diarization.json"]),
        resolve_session_file(session_dir, ["transcript.json"]),
    ]
    for path in candidates:
        segments = _load_segments_from_json(path)
        if segments:
            return segments
    return []


def _load_segments_from_json(path: Path) -> list[dict]:
    if not path.exists():
        return []

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError, json.JSONDecodeError):
        return []

    raw_segments = payload.get("segments", [])
    if not isinstance(raw_segments, list):
        return []

    segments: list[dict] = []
    for raw_segment in raw_segments:
        if not isinstance(raw_segment, dict):
            continue

        text = str(raw_segment.get("text", "")).strip()
        if not text:
            continue

        segments.append(
            {
                "start": _coerce_float(raw_segment.get("start")),
                "end": _coerce_float(raw_segment.get("end")),
                "text": text,
                "speaker": str(raw_segment.get("speaker", "")).strip() or None,
                "source": str(raw_segment.get("source", "")).strip() or None,
            }
        )
    return segments


def _coerce_float(value: object) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def _select_relevant_excerpts(question: str, transcript_segments: list[dict], limit: int = 6) -> list[dict]:
    normalized_question = " ".join(question.casefold().split())
    if not normalized_question or not transcript_segments:
        return []

    query_terms = [term for term in re.findall(r"[a-z0-9']+", normalized_question) if term not in _STOP_WORDS and len(term) > 1]
    scored: list[tuple[float, int]] = []
    for index, segment in enumerate(transcript_segments):
        score = _score_segment(segment, normalized_question, query_terms)
        if score > 0:
            scored.append((score, index))

    if not scored:
        return []

    scored.sort(key=lambda item: (-item[0], item[1]))
    chosen_indexes: set[int] = set()
    for _, index in scored[:limit]:
        chosen_indexes.add(index)
        if len(chosen_indexes) >= limit:
            break

    return [transcript_segments[index] for index in sorted(chosen_indexes)]


def _score_segment(segment: dict, normalized_question: str, query_terms: list[str]) -> float:
    text = str(segment.get("text", "")).casefold()
    speaker = str(segment.get("speaker", "") or "").casefold()
    haystack = f"{speaker} {text}".strip()
    if not haystack:
        return 0.0

    score = 0.0
    if normalized_question in haystack:
        score += 30.0

    for term in query_terms:
        if term in text:
            score += 8.0
        if speaker and term in speaker:
            score += 10.0

    if segment.get("speaker") and any(term in speaker for term in query_terms):
        score += 6.0

    return score


def _format_excerpt(segment: dict) -> str:
    start = _format_timestamp(float(segment.get("start", 0.0)))
    end = _format_timestamp(float(segment.get("end", 0.0)))
    speaker = str(segment.get("speaker", "") or "").strip()
    text = str(segment.get("text", "")).strip()
    if speaker:
        return f"[{start} - {end}] {speaker}: {text}"
    return f"[{start} - {end}] {text}"


def _format_timestamp(value: float) -> str:
    total_seconds = max(int(value), 0)
    hours = total_seconds // 3600
    minutes = (total_seconds % 3600) // 60
    seconds = total_seconds % 60
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}"
