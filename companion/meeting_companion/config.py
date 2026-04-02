from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import json
import os
import shutil


def _load_dotenv(workspace_root: Path) -> None:
    dotenv_path = workspace_root / ".env"
    if not dotenv_path.exists():
        return

    for raw_line in dotenv_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue

        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip()
        if not key or key in os.environ:
            continue

        if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
            value = value[1:-1]

        os.environ[key] = value


def _workspace_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _default_meetings_root() -> Path:
    return Path.home() / "Documents" / "Meetings"


def _resolve_config_path(workspace_root: Path) -> Path | None:
    configured = os.getenv("MEETING_RECORDER_CONFIG_PATH", "").strip()
    if configured:
        candidate = Path(configured)
        return candidate if candidate.is_absolute() else (workspace_root / candidate).resolve()

    candidate = workspace_root / "meeting-recorder.config.json"
    return candidate if candidate.exists() else None


def _load_json_config(config_path: Path | None) -> dict:
    if config_path is None or not config_path.exists():
        return {}

    try:
        return json.loads(config_path.read_text(encoding="utf-8"))
    except (OSError, ValueError, json.JSONDecodeError):
        return {}


def _config_value(config_data: dict, *keys: str, default=None):
    current: object = config_data
    for key in keys:
        if not isinstance(current, dict):
            return default
        current = current.get(key)
        if current is None:
            return default
    return current


def _resolve_path(workspace_root: Path, value: str | None) -> Path | None:
    if not value:
        return None
    path = Path(value)
    return path if path.is_absolute() else (workspace_root / path).resolve()


def _coerce_int(value: object, default: int) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def _coerce_float(value: object, default: float) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _coerce_str(value: object, default: str) -> str:
    if value is None:
        return default
    text = str(value).strip()
    return text or default


def _coerce_bool(value: object, default: bool) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in {"1", "true", "yes", "on"}:
            return True
        if normalized in {"0", "false", "no", "off"}:
            return False
        return default
    if isinstance(value, (int, float)):
        return bool(value)
    return default


def resolve_meetings_root(workspace_root: Path, configured: str | None = None) -> Path:
    env_value = os.getenv("MEETING_COMPANION_MEETINGS_ROOT")
    configured_path = env_value or configured
    candidates = [_resolve_path(workspace_root, configured_path)] if configured_path else [_default_meetings_root(), workspace_root / "meetings-data"]

    for candidate in candidates:
        if candidate is None:
            continue
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            probe = candidate / ".write-test"
            probe.write_text("ok", encoding="utf-8")
            probe.unlink(missing_ok=True)
            return candidate
        except OSError:
            continue

    fallback = workspace_root / "meetings-data"
    fallback.mkdir(parents=True, exist_ok=True)
    return fallback


def resolve_logs_root(workspace_root: Path, meetings_root: Path, configured: str | None = None) -> Path:
    env_value = os.getenv("MEETING_COMPANION_LOGS_ROOT")
    configured_path = _resolve_path(workspace_root, env_value or configured)
    candidates = [configured_path] if configured_path else [meetings_root / "logs"]
    for candidate in candidates:
        if candidate is None:
            continue
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            return candidate
        except OSError:
            continue

    fallback = workspace_root / "meetings-data" / "logs"
    fallback.mkdir(parents=True, exist_ok=True)
    return fallback


@dataclass(slots=True)
class CompanionConfig:
    host: str
    port: int
    workspace_root: Path
    companion_root: Path
    meetings_root: Path
    ffmpeg_path: str | None
    summary_provider: str
    ollama_url: str
    ollama_model: str
    openai_base_url: str
    openai_model: str
    openai_api_key: str | None
    whisper_gpu_model: str
    whisper_cpu_model: str
    startup_name: str
    app_logs_root: Path = Path.home() / "Documents" / "Meetings" / "logs"
    openai_summary_max_output_tokens: int = 900
    ollama_timeout_seconds: int = 60
    openai_timeout_seconds: int = 90
    summary_max_transcript_chars: int = 12000
    summary_max_highlights: int = 5
    summary_max_decisions: int = 5
    summary_max_action_items: int = 5
    whisper_live_gpu_model: str = "small"
    whisper_live_cpu_model: str = "base"
    normalized_sample_rate: int = 16000
    normalized_channels: int = 1
    live_language_hint: str = "en"
    full_transcription_task: str = "translate"
    live_transcription_task: str = "translate"
    vad_beam_size: int = 5
    fallback_beam_size: int = 1
    live_beam_size: int = 1
    live_best_of: int = 1
    live_captions_default_poll_seconds: float = 0.8
    live_captions_default_window_seconds: int = 4
    live_captions_display_line_count: int = 4
    live_captions_history_line_count: int = 8
    live_captions_recent_segment_count: int = 3
    live_captions_empty_passes_before_fallback: int = 3
    live_captions_clip_sample_rate: int = 16000
    live_captions_clip_channels: int = 1
    live_captions_enrichment_cooldown_seconds: float = 2.5
    history_default_search_limit: int = 10
    history_max_transcript_chars: int = 12000
    history_max_summary_chars: int = 5000
    history_max_relevant_excerpts: int = 6
    history_fallback_excerpt_count: int = 5
    history_openai_timeout_seconds: int = 90
    history_openai_max_output_tokens: int = 700
    automation_openai_timeout_seconds: int = 90
    automation_openai_max_output_tokens: int = 700
    automation_max_action_items: int = 10
    automation_max_transcript_chars: int = 12000
    processing_shutdown_join_timeout_seconds: int = 5

    # ── AI features ──────────────────────────────────────────────────
    # Embeddings
    embedding_model: str = "text-embedding-3-small"
    embedding_dimensions: int = 256
    embedding_chunk_words: int = 300
    embedding_chunk_overlap: int = 50

    # Smart titles
    titler_max_transcript_chars: int = 2000
    titler_openai_max_output_tokens: int = 50

    # Meeting type detection
    meeting_type_openai_max_output_tokens: int = 100

    # Agenda extraction
    agenda_max_topics: int = 10
    agenda_openai_max_output_tokens: int = 500

    # Highlights
    highlights_max_count: int = 5
    highlights_openai_max_output_tokens: int = 400

    # Follow-up (enhanced)
    followup_openai_max_output_tokens: int = 800

    # Commitments
    commitments_openai_max_output_tokens: int = 500
    commitments_max_items: int = 15

    # Meeting prep
    prep_max_past_sessions: int = 3
    prep_openai_max_output_tokens: int = 600

    # Topic timeline
    timeline_openai_max_output_tokens: int = 500

    # Sentiment analysis
    sentiment_openai_max_output_tokens: int = 300
    live_sentiment_enabled: bool = True
    live_sentiment_use_openai: bool = False

    # Speaker identification
    speaker_id_enabled: bool = True
    speaker_id_similarity_threshold: float = 0.75
    speaker_id_cluster_threshold: float = 0.88
    speaker_id_min_segment_seconds: float = 1.2


def resolve_ffmpeg_path(companion_root: Path, workspace_root: Path, configured: str | None = None) -> str | None:
    env_value = os.getenv("MEETING_COMPANION_FFMPEG")
    configured_path = _resolve_path(workspace_root, env_value or configured)
    if configured_path and configured_path.exists():
        return str(configured_path)

    bundled = companion_root / "bin" / "ffmpeg" / "ffmpeg.exe"
    if bundled.exists():
        return str(bundled)

    on_path = shutil.which("ffmpeg")
    if on_path:
        return on_path

    return None


def load_config() -> CompanionConfig:
    workspace_root = _workspace_root()
    _load_dotenv(workspace_root)
    config_path = _resolve_config_path(workspace_root)
    config_data = _load_json_config(config_path)

    companion_root = workspace_root / "companion"
    meetings_root = resolve_meetings_root(
        workspace_root,
        configured=_coerce_str(_config_value(config_data, "app", "meetingsRoot", default=""), ""),
    )
    app_logs_root = resolve_logs_root(
        workspace_root,
        meetings_root,
        configured=_coerce_str(_config_value(config_data, "app", "logsRoot", default=""), ""),
    )

    openai_api_key = os.getenv("OPENAI_API_KEY")
    configured_provider = os.getenv("MEETING_COMPANION_SUMMARY_PROVIDER", "").strip().lower()
    if not configured_provider:
        configured_provider = _coerce_str(_config_value(config_data, "companion", "summary", "provider", default=""), "").lower()
    if configured_provider:
        summary_provider = configured_provider
    else:
        summary_provider = "openai" if openai_api_key else "ollama"

    configured_openai_model = os.getenv("OPENAI_MODEL", "").strip() or _coerce_str(
        _config_value(config_data, "companion", "summary", "openaiModel", default="gpt-5-mini"),
        "gpt-5-mini",
    )
    if "realtime" in configured_openai_model.lower():
        configured_openai_model = "gpt-5-mini"

    return CompanionConfig(
        host=os.getenv(
            "MEETING_COMPANION_HOST",
            _coerce_str(_config_value(config_data, "companion", "server", "host", default="127.0.0.1"), "127.0.0.1"),
        ),
        port=int(
            os.getenv(
                "MEETING_COMPANION_PORT",
                str(_coerce_int(_config_value(config_data, "companion", "server", "port", default=17823), 17823)),
            )
        ),
        workspace_root=workspace_root,
        companion_root=companion_root,
        meetings_root=meetings_root,
        app_logs_root=app_logs_root,
        ffmpeg_path=resolve_ffmpeg_path(
            companion_root,
            workspace_root,
            configured=_coerce_str(_config_value(config_data, "app", "ffmpegPath", default=""), ""),
        ),
        summary_provider=summary_provider,
        ollama_url=os.getenv(
            "OLLAMA_URL",
            _coerce_str(_config_value(config_data, "companion", "summary", "ollamaUrl", default="http://127.0.0.1:11434"), "http://127.0.0.1:11434"),
        ),
        ollama_model=os.getenv(
            "OLLAMA_MODEL",
            _coerce_str(_config_value(config_data, "companion", "summary", "ollamaModel", default="qwen2.5:7b-instruct"), "qwen2.5:7b-instruct"),
        ),
        openai_base_url=os.getenv(
            "OPENAI_BASE_URL",
            _coerce_str(_config_value(config_data, "companion", "summary", "openaiBaseUrl", default="https://api.openai.com/v1"), "https://api.openai.com/v1"),
        ),
        openai_model=configured_openai_model,
        openai_api_key=openai_api_key,
        whisper_gpu_model=os.getenv(
            "MEETING_COMPANION_WHISPER_GPU_MODEL",
            _coerce_str(_config_value(config_data, "companion", "transcription", "gpuModel", default="small"), "small"),
        ),
        whisper_cpu_model=os.getenv(
            "MEETING_COMPANION_WHISPER_CPU_MODEL",
            _coerce_str(_config_value(config_data, "companion", "transcription", "cpuModel", default="base"), "base"),
        ),
        startup_name=_coerce_str(
            _config_value(config_data, "companion", "server", "startupName", default="MeetingRecorderCompanion"),
            "MeetingRecorderCompanion",
        ),
        openai_summary_max_output_tokens=_coerce_int(
            _config_value(config_data, "companion", "summary", "openaiMaxOutputTokens", default=900),
            900,
        ),
        ollama_timeout_seconds=_coerce_int(
            _config_value(config_data, "companion", "summary", "ollamaTimeoutSeconds", default=60),
            60,
        ),
        openai_timeout_seconds=_coerce_int(
            _config_value(config_data, "companion", "summary", "openaiTimeoutSeconds", default=90),
            90,
        ),
        summary_max_transcript_chars=_coerce_int(
            _config_value(config_data, "companion", "summary", "maxTranscriptChars", default=12000),
            12000,
        ),
        summary_max_highlights=_coerce_int(
            _config_value(config_data, "companion", "summary", "maxHighlightSentences", default=5),
            5,
        ),
        summary_max_decisions=_coerce_int(
            _config_value(config_data, "companion", "summary", "maxDecisionSentences", default=5),
            5,
        ),
        summary_max_action_items=_coerce_int(
            _config_value(config_data, "companion", "summary", "maxActionItemSentences", default=5),
            5,
        ),
        whisper_live_gpu_model=_coerce_str(
            _config_value(config_data, "companion", "transcription", "liveGpuModel", default="small"),
            "small",
        ),
        whisper_live_cpu_model=_coerce_str(
            _config_value(config_data, "companion", "transcription", "liveCpuModel", default="base"),
            "base",
        ),
        normalized_sample_rate=_coerce_int(
            _config_value(config_data, "companion", "transcription", "normalizedSampleRate", default=16000),
            16000,
        ),
        normalized_channels=_coerce_int(
            _config_value(config_data, "companion", "transcription", "normalizedChannels", default=1),
            1,
        ),
        live_language_hint=_coerce_str(
            _config_value(config_data, "companion", "transcription", "liveLanguageHint", default="en"),
            "en",
        ),
        full_transcription_task=_coerce_str(
            _config_value(config_data, "companion", "transcription", "fullTask", default="translate"),
            "translate",
        ),
        live_transcription_task=_coerce_str(
            _config_value(config_data, "companion", "transcription", "liveTask", default="translate"),
            "translate",
        ),
        vad_beam_size=_coerce_int(
            _config_value(config_data, "companion", "transcription", "vadBeamSize", default=5),
            5,
        ),
        fallback_beam_size=_coerce_int(
            _config_value(config_data, "companion", "transcription", "fallbackBeamSize", default=1),
            1,
        ),
        live_beam_size=_coerce_int(
            _config_value(config_data, "companion", "transcription", "liveBeamSize", default=1),
            1,
        ),
        live_best_of=_coerce_int(
            _config_value(config_data, "companion", "transcription", "liveBestOf", default=1),
            1,
        ),
        live_captions_default_poll_seconds=_coerce_float(
            _config_value(config_data, "companion", "liveCaptions", "defaultPollSeconds", default=0.8),
            0.8,
        ),
        live_captions_default_window_seconds=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "defaultWindowSeconds", default=4),
            4,
        ),
        live_captions_display_line_count=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "displayLineCount", default=4),
            4,
        ),
        live_captions_history_line_count=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "historyLineCount", default=8),
            8,
        ),
        live_captions_recent_segment_count=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "recentSegmentCount", default=3),
            3,
        ),
        live_captions_empty_passes_before_fallback=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "emptyPassesBeforeFallback", default=3),
            3,
        ),
        live_captions_clip_sample_rate=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "clipSampleRate", default=16000),
            16000,
        ),
        live_captions_clip_channels=_coerce_int(
            _config_value(config_data, "companion", "liveCaptions", "clipChannels", default=1),
            1,
        ),
        live_captions_enrichment_cooldown_seconds=_coerce_float(
            _config_value(config_data, "companion", "liveCaptions", "enrichmentCooldownSeconds", default=2.5),
            2.5,
        ),
        history_default_search_limit=_coerce_int(
            _config_value(config_data, "companion", "history", "defaultSearchLimit", default=10),
            10,
        ),
        history_max_transcript_chars=_coerce_int(
            _config_value(config_data, "companion", "history", "maxTranscriptChars", default=12000),
            12000,
        ),
        history_max_summary_chars=_coerce_int(
            _config_value(config_data, "companion", "history", "maxSummaryChars", default=5000),
            5000,
        ),
        history_max_relevant_excerpts=_coerce_int(
            _config_value(config_data, "companion", "history", "maxRelevantExcerpts", default=6),
            6,
        ),
        history_fallback_excerpt_count=_coerce_int(
            _config_value(config_data, "companion", "history", "fallbackExcerptCount", default=5),
            5,
        ),
        history_openai_timeout_seconds=_coerce_int(
            _config_value(config_data, "companion", "history", "openaiTimeoutSeconds", default=90),
            90,
        ),
        history_openai_max_output_tokens=_coerce_int(
            _config_value(config_data, "companion", "history", "openaiMaxOutputTokens", default=700),
            700,
        ),
        automation_openai_timeout_seconds=_coerce_int(
            _config_value(config_data, "companion", "automation", "openaiTimeoutSeconds", default=90),
            90,
        ),
        automation_openai_max_output_tokens=_coerce_int(
            _config_value(config_data, "companion", "automation", "openaiMaxOutputTokens", default=700),
            700,
        ),
        automation_max_action_items=_coerce_int(
            _config_value(config_data, "companion", "automation", "maxActionItems", default=10),
            10,
        ),
        automation_max_transcript_chars=_coerce_int(
            _config_value(config_data, "companion", "automation", "maxTranscriptChars", default=12000),
            12000,
        ),
        processing_shutdown_join_timeout_seconds=_coerce_int(
            _config_value(config_data, "companion", "processing", "shutdownJoinTimeoutSeconds", default=5),
            5,
        ),
        sentiment_openai_max_output_tokens=_coerce_int(
            _config_value(config_data, "companion", "sentiment", "openaiMaxOutputTokens", default=300),
            300,
        ),
        live_sentiment_enabled=_coerce_bool(
            _config_value(config_data, "companion", "sentiment", "liveEnabled", default=True),
            True,
        ),
        live_sentiment_use_openai=_coerce_bool(
            _config_value(config_data, "companion", "sentiment", "liveUseOpenAi", default=False),
            False,
        ),
        speaker_id_enabled=bool(
            _config_value(config_data, "companion", "speakerIdentification", "enabled", default=True)
        ),
        speaker_id_similarity_threshold=_coerce_float(
            _config_value(config_data, "companion", "speakerIdentification", "similarityThreshold", default=0.75),
            0.75,
        ),
        speaker_id_cluster_threshold=_coerce_float(
            _config_value(config_data, "companion", "speakerIdentification", "clusterThreshold", default=0.88),
            0.88,
        ),
        speaker_id_min_segment_seconds=_coerce_float(
            _config_value(config_data, "companion", "speakerIdentification", "minSegmentSeconds", default=1.2),
            1.2,
        ),
    )
