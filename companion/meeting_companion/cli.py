from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

from .config import load_config
from .history import ask_meeting_question, search_meetings
from .logging_utils import configure_application_logging
from .pipeline.live_captions import LiveCaptionStreamer
from .pipeline.processor import SessionProcessor
from .storage import SessionStorage

# Lazy imports for AI features are done in command handlers below.


def _configure_console_output() -> None:
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="replace")


def build_parser(config) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Meeting recorder companion CLI")
    subcommands = parser.add_subparsers(dest="command")

    serve_parser = subcommands.add_parser("serve", help="Run the legacy HTTP server and tray app.")
    serve_parser.add_argument("--headless", action="store_true", help="Run without the Windows tray icon.")
    serve_parser.add_argument("--no-startup", action="store_true", help="Do not register the app to start on login.")

    process_parser = subcommands.add_parser("process-session", help="Process an existing captured session folder.")
    process_parser.add_argument("--session-dir", required=True, help="Path to the session directory that contains metadata.json.")

    captions_parser = subcommands.add_parser("live-captions", help="Stream live captions for an active session folder.")
    captions_parser.add_argument("--session-dir", required=True, help="Path to the active session directory.")
    captions_parser.add_argument("--poll-seconds", type=float, default=config.live_captions_default_poll_seconds, help="How often to refresh captions.")
    captions_parser.add_argument("--window-seconds", type=int, default=config.live_captions_default_window_seconds, help="How much recent audio to transcribe.")

    history_parser = subcommands.add_parser("search-history", help="Search saved meeting summaries and transcripts.")
    history_parser.add_argument("--query", required=True, help="Search query text.")
    history_parser.add_argument("--limit", type=int, default=config.history_default_search_limit, help="Maximum number of results.")

    ask_parser = subcommands.add_parser("ask-meeting", help="Ask a question about one saved meeting.")
    ask_parser.add_argument("--session-dir", required=True, help="Path to the meeting session directory.")
    ask_parser.add_argument("--question", required=True, help="Question to ask about the meeting.")

    # AI feature commands
    index_session_parser = subcommands.add_parser("index-session", help="Index one session into the embedding store.")
    index_session_parser.add_argument("--session-dir", required=True, help="Path to the session directory.")

    subcommands.add_parser("index-all", help="Index all unindexed sessions into the embedding store.")

    semantic_parser = subcommands.add_parser("semantic-search", help="Cross-meeting semantic search.")
    semantic_parser.add_argument("--query", required=True, help="Search query.")
    semantic_parser.add_argument("--limit", type=int, default=10, help="Max results.")

    agenda_parser = subcommands.add_parser("extract-agenda", help="Extract agenda from a session.")
    agenda_parser.add_argument("--session-dir", required=True, help="Path to the session directory.")

    highlights_parser = subcommands.add_parser("extract-highlights", help="Extract highlight moments from a session.")
    highlights_parser.add_argument("--session-dir", required=True, help="Path to the session directory.")

    commitments_parser = subcommands.add_parser("extract-commitments", help="Extract commitments from a session.")
    commitments_parser.add_argument("--session-dir", required=True, help="Path to the session directory.")

    open_commitments_parser = subcommands.add_parser("open-commitments", help="List open commitments across meetings.")
    open_commitments_parser.add_argument("--limit", type=int, default=20, help="Max results.")

    prep_parser = subcommands.add_parser("meeting-prep", help="Generate a prep brief for an upcoming meeting.")
    prep_parser.add_argument("--title", required=True, help="Meeting title to prepare for.")
    prep_parser.add_argument("--limit", type=int, default=3, help="Max past sessions to include.")

    timeline_parser = subcommands.add_parser("topic-timeline", help="Track topic evolution across meetings.")
    timeline_parser.add_argument("--query", required=True, help="Topic to track.")
    timeline_parser.add_argument("--limit", type=int, default=10, help="Max sessions.")

    return parser


def process_session(session_dir: Path) -> int:
    config = load_config()
    storage = SessionStorage(config.meetings_root)
    processor = SessionProcessor(config, storage, autostart_worker=False)
    session = storage.load_session(session_dir)
    processor.process_session(session)
    return 0


def run_live_captions(session_dir: Path, poll_seconds: float, window_seconds: int) -> int:
    config = load_config()
    storage = SessionStorage(config.meetings_root)
    streamer = LiveCaptionStreamer(config, storage)
    return streamer.run(session_dir, poll_seconds=poll_seconds, window_seconds=window_seconds)


def search_history(query: str, limit: int) -> int:
    config = load_config()
    storage = SessionStorage(config.meetings_root)
    results = search_meetings(storage, query, limit=limit)
    print(json.dumps({"results": results}, indent=2))
    return 0


def ask_meeting(session_dir: Path, question: str) -> int:
    config = load_config()
    answer = ask_meeting_question(config, session_dir, question)
    print(answer)
    return 0


def main(argv: list[str] | None = None) -> int:
    _configure_console_output()
    config = load_config()
    configure_application_logging(config, console=True)
    parser = build_parser(config)
    args = parser.parse_args(argv)

    if args.command is None or args.command == "serve":
        from .app import main as serve_main

        serve_args: list[str] = []
        if getattr(args, "headless", False):
            serve_args.append("--headless")
        if getattr(args, "no_startup", False):
            serve_args.append("--no-startup")
        return serve_main(serve_args)

    if args.command == "process-session":
        return process_session(Path(args.session_dir))

    if args.command == "live-captions":
        return run_live_captions(Path(args.session_dir), float(args.poll_seconds), int(args.window_seconds))

    if args.command == "search-history":
        return search_history(str(args.query), int(args.limit))

    if args.command == "ask-meeting":
        return ask_meeting(Path(args.session_dir), str(args.question))

    if args.command == "index-session":
        return _cmd_index_session(Path(args.session_dir))

    if args.command == "index-all":
        return _cmd_index_all()

    if args.command == "semantic-search":
        return _cmd_semantic_search(str(args.query), int(args.limit))

    if args.command == "extract-agenda":
        return _cmd_extract_agenda(Path(args.session_dir))

    if args.command == "extract-highlights":
        return _cmd_extract_highlights(Path(args.session_dir))

    if args.command == "extract-commitments":
        return _cmd_extract_commitments(Path(args.session_dir))

    if args.command == "open-commitments":
        return _cmd_open_commitments(int(args.limit))

    if args.command == "meeting-prep":
        return _cmd_meeting_prep(str(args.title), int(args.limit))

    if args.command == "topic-timeline":
        return _cmd_topic_timeline(str(args.query), int(args.limit))

    parser.error(f"Unsupported command: {args.command}")
    return 2


# ── AI feature command handlers ──────────────────────────────────────

def _cmd_index_session(session_dir: Path) -> int:
    config = load_config()
    from .embeddings.indexer import index_session
    from .embeddings.store import EmbeddingStore
    store = EmbeddingStore(config.meetings_root / "embeddings.db")
    count = index_session(config, store, session_dir, force=True)
    store.close()
    print(json.dumps({"indexed_chunks": count}))
    return 0


def _cmd_index_all() -> int:
    config = load_config()
    from .embeddings.indexer import index_all_sessions
    from .embeddings.store import EmbeddingStore
    store = EmbeddingStore(config.meetings_root / "embeddings.db")
    count = index_all_sessions(config, store)
    store.close()
    print(json.dumps({"total_indexed_chunks": count, "sessions": store.get_session_count() if hasattr(store, 'get_session_count') else 0}))
    return 0


def _cmd_semantic_search(query: str, limit: int) -> int:
    config = load_config()
    from .embeddings.search import semantic_search
    from .embeddings.store import EmbeddingStore
    store = EmbeddingStore(config.meetings_root / "embeddings.db")
    results = semantic_search(config, store, query, top_k=limit)
    store.close()
    print(json.dumps({"results": results}, indent=2))
    return 0


def _cmd_extract_agenda(session_dir: Path) -> int:
    config = load_config()
    from .pipeline.agenda import extract_agenda
    storage = SessionStorage(config.meetings_root)
    session = storage.load_session(session_dir)
    transcript = _load_transcript_for_session(config, session_dir)
    result = extract_agenda(config, transcript)
    (session_dir / "agenda.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0


def _cmd_extract_highlights(session_dir: Path) -> int:
    config = load_config()
    from .pipeline.highlights import extract_highlights
    transcript = _load_transcript_for_session(config, session_dir)
    summary = ""
    summary_path = session_dir / "summary.md"
    if summary_path.exists():
        summary = summary_path.read_text(encoding="utf-8")
    agenda = None
    agenda_path = session_dir / "agenda.json"
    if agenda_path.exists():
        agenda = json.loads(agenda_path.read_text(encoding="utf-8"))
    result = extract_highlights(config, transcript, summary, agenda)
    (session_dir / "highlights.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0


def _cmd_extract_commitments(session_dir: Path) -> int:
    config = load_config()
    from .pipeline.commitments import extract_commitments
    from .models import SessionStartRequest
    transcript = _load_transcript_for_session(config, session_dir)
    metadata = json.loads((session_dir / "metadata.json").read_text(encoding="utf-8"))
    meeting = SessionStartRequest.from_metadata(metadata)
    items = extract_commitments(config, transcript, meeting)
    result = {"items": items}
    (session_dir / "commitments.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0


def _cmd_open_commitments(limit: int) -> int:
    config = load_config()
    from .pipeline.commitments import get_open_commitments
    items = get_open_commitments(config, limit=limit)
    print(json.dumps({"commitments": items}, indent=2))
    return 0


def _cmd_meeting_prep(title: str, limit: int) -> int:
    config = load_config()
    config_override = config
    if limit != config.prep_max_past_sessions:
        import dataclasses
        config_override = dataclasses.replace(config, prep_max_past_sessions=limit)
    from .pipeline.prep import generate_prep_brief
    brief = generate_prep_brief(config_override, title)
    print(brief)
    return 0


def _cmd_topic_timeline(query: str, limit: int) -> int:
    config = load_config()
    from .pipeline.topic_timeline import build_topic_timeline
    result = build_topic_timeline(config, query, limit=limit)
    print(json.dumps(result, indent=2))
    return 0


def _load_transcript_for_session(config, session_dir: Path):
    """Load transcript from session directory into a TranscriptResult."""
    from .models import TranscriptResult, TranscriptSegment
    transcript_path = session_dir / "_session" / "transcript.json"
    if not transcript_path.exists():
        transcript_path = session_dir / "transcript.json"
    if not transcript_path.exists():
        text_path = session_dir / "transcript.txt"
        text = text_path.read_text(encoding="utf-8") if text_path.exists() else ""
        return TranscriptResult(status="completed", text=text, segments=[])

    payload = json.loads(transcript_path.read_text(encoding="utf-8"))
    segments = [
        TranscriptSegment(
            start=float(s.get("start", 0)),
            end=float(s.get("end", 0)),
            text=str(s.get("text", "")),
            speaker=s.get("speaker"),
            source=s.get("source"),
        )
        for s in payload.get("segments", [])
        if s.get("text")
    ]
    return TranscriptResult(
        status=payload.get("status", "completed"),
        text=payload.get("text", ""),
        segments=segments,
        language=payload.get("language"),
        model=payload.get("model"),
    )
