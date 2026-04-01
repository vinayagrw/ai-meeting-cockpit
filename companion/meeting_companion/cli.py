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

    parser.error(f"Unsupported command: {args.command}")
    return 2
