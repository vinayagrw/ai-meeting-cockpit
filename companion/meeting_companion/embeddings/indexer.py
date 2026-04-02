"""Index session transcripts into the embedding store as overlapping chunks."""

from __future__ import annotations

import json
import logging
from pathlib import Path

from ..config import CompanionConfig
from ..pipeline.openai_client import embed_texts
from .store import EmbeddingStore

logger = logging.getLogger(__name__)


def chunk_transcript(segments: list[dict], chunk_words: int = 300, overlap_words: int = 50) -> list[dict]:
    """Split transcript segments into overlapping word-based chunks."""
    if not segments:
        return []

    all_tokens: list[dict] = []
    for segment in segments:
        text = str(segment.get("text", "")).strip()
        if not text:
            continue
        words = text.split()
        for word in words:
            all_tokens.append({
                "word": word,
                "speaker": segment.get("speaker"),
                "start_sec": segment.get("start"),
                "end_sec": segment.get("end"),
            })

    if not all_tokens:
        return []

    chunks: list[dict] = []
    step = max(1, chunk_words - overlap_words)
    for i in range(0, len(all_tokens), step):
        window = all_tokens[i : i + chunk_words]
        if not window:
            break

        text = " ".join(t["word"] for t in window)
        speakers = sorted({t["speaker"] for t in window if t.get("speaker")})
        start_sec = window[0].get("start_sec")
        end_sec = window[-1].get("end_sec")

        chunks.append({
            "chunk_index": len(chunks),
            "text": text,
            "speaker": "; ".join(speakers) if speakers else None,
            "start_sec": start_sec,
            "end_sec": end_sec,
        })

        if i + chunk_words >= len(all_tokens):
            break

    return chunks


def _load_transcript_segments(session_dir: Path) -> list[dict]:
    """Load transcript segments from session directory."""
    for filename in ["speaker-diarization.json", "transcript.json"]:
        candidates = [session_dir / "_session" / filename, session_dir / filename]
        for path in candidates:
            if not path.exists():
                continue
            try:
                payload = json.loads(path.read_text(encoding="utf-8"))
                segments = payload.get("segments", [])
                if segments:
                    return segments
            except (OSError, ValueError, json.JSONDecodeError):
                continue
    return []


def index_session(config: CompanionConfig, store: EmbeddingStore, session_dir: Path, force: bool = False) -> int:
    """Index a single session. Returns number of chunks indexed."""
    session_key = str(session_dir)

    if not force and store.has_session(session_key):
        return 0

    segments = _load_transcript_segments(session_dir)
    if not segments:
        logger.info("No transcript segments found for %s", session_dir.name)
        return 0

    chunks = chunk_transcript(
        segments,
        chunk_words=config.embedding_chunk_words,
        overlap_words=config.embedding_chunk_overlap,
    )
    if not chunks:
        return 0

    texts = [chunk["text"] for chunk in chunks]

    try:
        embeddings = embed_texts(config, texts)
    except Exception:
        logger.warning("Embedding failed for %s, skipping", session_dir.name, exc_info=True)
        return 0

    for chunk, embedding in zip(chunks, embeddings):
        chunk["embedding"] = embedding

    store.insert_chunks(session_key, chunks)
    logger.info("Indexed %d chunks for %s", len(chunks), session_dir.name)
    return len(chunks)


def index_all_sessions(config: CompanionConfig, store: EmbeddingStore, force: bool = False) -> int:
    """Index all unindexed sessions. Returns total chunks indexed."""
    total = 0
    meetings_root = config.meetings_root
    if not meetings_root.exists():
        return 0

    for session_dir in sorted(meetings_root.glob("*/*")):
        metadata_path = session_dir / "metadata.json"
        if not metadata_path.exists():
            continue
        total += index_session(config, store, session_dir, force=force)

    return total
