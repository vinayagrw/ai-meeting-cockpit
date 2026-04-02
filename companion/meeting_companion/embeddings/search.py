"""Semantic search across all indexed sessions."""

from __future__ import annotations

import json
import logging
from collections import defaultdict
from pathlib import Path

from ..config import CompanionConfig
from ..pipeline.openai_client import embed_texts
from .store import EmbeddingStore

logger = logging.getLogger(__name__)


def semantic_search(
    config: CompanionConfig,
    store: EmbeddingStore,
    query: str,
    top_k: int = 10,
) -> list[dict]:
    """Search across all indexed sessions using embedding similarity.

    Returns results grouped by session with excerpts.
    """
    if not query.strip():
        return []

    try:
        query_embeddings = embed_texts(config, [query.strip()])
        query_embedding = query_embeddings[0]
    except Exception:
        logger.warning("Failed to embed query, falling back to empty results", exc_info=True)
        return []

    raw_results = store.search(query_embedding, top_k=top_k * 3)

    session_groups: dict[str, list[dict]] = defaultdict(list)
    for result in raw_results:
        session_groups[result["session_dir"]].append(result)

    ranked_sessions: list[dict] = []
    for session_dir_str, chunks in session_groups.items():
        session_dir = Path(session_dir_str)
        metadata = _load_metadata(session_dir)
        best_similarity = max(c["similarity"] for c in chunks)
        excerpts = sorted(chunks, key=lambda c: -c["similarity"])[:3]

        ranked_sessions.append({
            "sessionDir": session_dir_str,
            "title": metadata.get("title", session_dir.name),
            "platform": metadata.get("platform"),
            "startedAt": metadata.get("startedAt"),
            "relevance": best_similarity,
            "excerpts": [
                {
                    "text": e["text"][:300],
                    "speaker": e["speaker"],
                    "startSec": e["start_sec"],
                    "endSec": e["end_sec"],
                    "similarity": e["similarity"],
                }
                for e in excerpts
            ],
        })

    ranked_sessions.sort(key=lambda s: -s["relevance"])
    return ranked_sessions[:top_k]


def _load_metadata(session_dir: Path) -> dict:
    metadata_path = session_dir / "metadata.json"
    if not metadata_path.exists():
        return {}
    try:
        return json.loads(metadata_path.read_text(encoding="utf-8"))
    except (OSError, ValueError, json.JSONDecodeError):
        return {}
