"""SQLite-backed vector store for transcript chunk embeddings."""

from __future__ import annotations

import json
import logging
import math
import sqlite3
from pathlib import Path

logger = logging.getLogger(__name__)

_SCHEMA = """
CREATE TABLE IF NOT EXISTS chunks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_dir TEXT NOT NULL,
    chunk_index INTEGER NOT NULL,
    text TEXT NOT NULL,
    speaker TEXT,
    start_sec REAL,
    end_sec REAL,
    embedding BLOB NOT NULL,
    UNIQUE(session_dir, chunk_index)
);

CREATE INDEX IF NOT EXISTS idx_chunks_session ON chunks(session_dir);
"""


class EmbeddingStore:
    def __init__(self, db_path: Path) -> None:
        self.db_path = db_path
        db_path.parent.mkdir(parents=True, exist_ok=True)
        self._conn = sqlite3.connect(str(db_path), check_same_thread=False)
        self._conn.executescript(_SCHEMA)

    def close(self) -> None:
        self._conn.close()

    def has_session(self, session_dir: str) -> bool:
        row = self._conn.execute(
            "SELECT 1 FROM chunks WHERE session_dir = ? LIMIT 1", (session_dir,)
        ).fetchone()
        return row is not None

    def delete_session(self, session_dir: str) -> None:
        self._conn.execute("DELETE FROM chunks WHERE session_dir = ?", (session_dir,))
        self._conn.commit()

    def insert_chunks(self, session_dir: str, chunks: list[dict]) -> None:
        self.delete_session(session_dir)
        self._conn.executemany(
            "INSERT INTO chunks (session_dir, chunk_index, text, speaker, start_sec, end_sec, embedding) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            [
                (
                    session_dir,
                    chunk["chunk_index"],
                    chunk["text"],
                    chunk.get("speaker"),
                    chunk.get("start_sec"),
                    chunk.get("end_sec"),
                    _encode_embedding(chunk["embedding"]),
                )
                for chunk in chunks
            ],
        )
        self._conn.commit()

    def search(self, query_embedding: list[float], top_k: int = 10) -> list[dict]:
        rows = self._conn.execute(
            "SELECT session_dir, chunk_index, text, speaker, start_sec, end_sec, embedding FROM chunks"
        ).fetchall()

        scored: list[tuple[float, dict]] = []
        for row in rows:
            session_dir, chunk_index, text, speaker, start_sec, end_sec, emb_blob = row
            stored_embedding = _decode_embedding(emb_blob)
            similarity = _cosine_similarity(query_embedding, stored_embedding)
            scored.append(
                (
                    similarity,
                    {
                        "session_dir": session_dir,
                        "chunk_index": chunk_index,
                        "text": text,
                        "speaker": speaker,
                        "start_sec": start_sec,
                        "end_sec": end_sec,
                        "similarity": round(similarity, 4),
                    },
                )
            )

        scored.sort(key=lambda x: -x[0])
        return [item for _, item in scored[:top_k]]

    def get_session_count(self) -> int:
        row = self._conn.execute("SELECT COUNT(DISTINCT session_dir) FROM chunks").fetchone()
        return row[0] if row else 0

    def get_chunk_count(self) -> int:
        row = self._conn.execute("SELECT COUNT(*) FROM chunks").fetchone()
        return row[0] if row else 0


def _encode_embedding(embedding: list[float]) -> bytes:
    return json.dumps(embedding).encode("utf-8")


def _decode_embedding(blob: bytes) -> list[float]:
    return json.loads(blob.decode("utf-8"))


def _cosine_similarity(a: list[float], b: list[float]) -> float:
    if len(a) != len(b):
        return 0.0
    dot = sum(x * y for x, y in zip(a, b))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(x * x for x in b))
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return dot / (norm_a * norm_b)
