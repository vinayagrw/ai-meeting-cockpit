"""Shared OpenAI HTTP client — single call point for all features."""

from __future__ import annotations

import json
import logging
from urllib import error, request

from ..config import CompanionConfig

logger = logging.getLogger(__name__)


def _extract_output_text(body: dict) -> str:
    """Extract the text from an OpenAI /responses payload."""
    text = str(body.get("output_text", "")).strip()
    if text:
        return text

    for output_item in body.get("output", []) or []:
        for content_item in output_item.get("content", []) or []:
            if content_item.get("type") == "output_text":
                text = str(content_item.get("text", "")).strip()
                if text:
                    return text
    return ""


def call_openai(
    config: CompanionConfig,
    instructions: str,
    prompt: str,
    max_tokens: int,
    *,
    timeout: int | None = None,
) -> str:
    """Call OpenAI /responses endpoint. Returns output text. Raises on failure."""
    if not config.openai_api_key:
        raise ValueError("OPENAI_API_KEY is not configured")

    payload = json.dumps(
        {
            "model": config.openai_model,
            "instructions": instructions,
            "input": prompt,
            "max_output_tokens": max_tokens,
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

    effective_timeout = timeout or config.openai_timeout_seconds
    with request.urlopen(http_request, timeout=effective_timeout) as response:
        body = json.loads(response.read().decode("utf-8"))

    text = _extract_output_text(body)
    if not text:
        raise ValueError("OpenAI returned an empty response")
    return text


def call_openai_json(
    config: CompanionConfig,
    instructions: str,
    prompt: str,
    max_tokens: int,
    *,
    timeout: int | None = None,
) -> dict:
    """Call OpenAI and parse the response as JSON. Raises on failure."""
    text = call_openai(config, instructions, prompt, max_tokens, timeout=timeout)
    return json.loads(text)


def call_openai_safe(
    config: CompanionConfig,
    instructions: str,
    prompt: str,
    max_tokens: int,
    *,
    timeout: int | None = None,
    fallback: str = "",
) -> str:
    """Call OpenAI with automatic fallback on any error."""
    try:
        return call_openai(config, instructions, prompt, max_tokens, timeout=timeout)
    except (error.URLError, error.HTTPError, TimeoutError, OSError, ValueError, json.JSONDecodeError) as exc:
        logger.warning("OpenAI call failed, using fallback: %s", exc)
        return fallback


def call_openai_json_safe(
    config: CompanionConfig,
    instructions: str,
    prompt: str,
    max_tokens: int,
    *,
    timeout: int | None = None,
    fallback: dict | None = None,
) -> dict:
    """Call OpenAI and parse JSON with automatic fallback on any error."""
    try:
        return call_openai_json(config, instructions, prompt, max_tokens, timeout=timeout)
    except (error.URLError, error.HTTPError, TimeoutError, OSError, ValueError, json.JSONDecodeError) as exc:
        logger.warning("OpenAI JSON call failed, using fallback: %s", exc)
        return fallback if fallback is not None else {}


def embed_texts(
    config: CompanionConfig,
    texts: list[str],
    *,
    timeout: int | None = None,
) -> list[list[float]]:
    """Call OpenAI embeddings endpoint. Returns list of embedding vectors."""
    if not config.openai_api_key:
        raise ValueError("OPENAI_API_KEY is not configured")

    payload = json.dumps(
        {
            "model": config.embedding_model,
            "input": texts,
            "dimensions": config.embedding_dimensions,
        }
    ).encode("utf-8")

    http_request = request.Request(
        f"{config.openai_base_url.rstrip('/')}/embeddings",
        data=payload,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {config.openai_api_key}",
        },
        method="POST",
    )

    effective_timeout = timeout or config.openai_timeout_seconds
    with request.urlopen(http_request, timeout=effective_timeout) as response:
        body = json.loads(response.read().decode("utf-8"))

    data = body.get("data", [])
    return [item["embedding"] for item in sorted(data, key=lambda x: x["index"])]
