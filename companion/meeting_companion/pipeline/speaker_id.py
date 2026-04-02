from __future__ import annotations

from array import array
from collections import Counter
from dataclasses import asdict, dataclass
from datetime import datetime
import json
import math
from pathlib import Path
import subprocess
import wave

from ..config import CompanionConfig
from ..models import FinalizedSession, TranscriptSegment
from ..storage import session_artifact_path
from .diarization import SpeakerDiarizationResult

_GENERIC_SPEAKER_PREFIXES = ("speaker ", "voice ")
_GENERIC_SPEAKER_VALUES = {"meeting", "unknown", "participant", "remote", "attendee"}
_VOICE_ID_TEMPLATE = "Voice {number:03d}"


@dataclass(slots=True)
class SpeakerProfile:
    profile_id: str
    display_name: str
    embedding: list[float]
    sample_count: int
    session_keys: list[str]
    aliases: list[str]
    first_seen: str
    last_seen: str
    platforms: list[str]
    example_sessions: list[str]

    def as_dict(self) -> dict:
        return asdict(self)


@dataclass(slots=True)
class SpeakerIdentityAssignment:
    speaker_label: str
    profile_id: str
    similarity: float
    session_segment_count: int
    aliases: list[str]

    def as_dict(self) -> dict:
        return asdict(self)


@dataclass(slots=True)
class SpeakerIdentityResult:
    status: str
    store_path: str
    assignments: list[SpeakerIdentityAssignment]
    profiles: list[SpeakerProfile]
    error: str | None = None

    def as_dict(self) -> dict:
        return {
            "status": self.status,
            "storePath": self.store_path,
            "assignments": [assignment.as_dict() for assignment in self.assignments],
            "profiles": [profile.as_dict() for profile in self.profiles],
            "error": self.error,
        }


@dataclass(slots=True)
class _SpeakerCluster:
    indices: list[int]
    centroid: list[float]
    explicit_names: list[str]


def apply_recurring_speaker_identities(
    config: CompanionConfig,
    session: FinalizedSession,
    diarization: SpeakerDiarizationResult,
) -> SpeakerIdentityResult:
    store_path = config.meetings_root / "speaker-profiles.json"
    if not config.speaker_id_enabled:
        return SpeakerIdentityResult("disabled", str(store_path), [], [])

    if not diarization.segments:
        return SpeakerIdentityResult("unavailable", str(store_path), [], [], "No speaker segments were available.")

    if not config.ffmpeg_path:
        return SpeakerIdentityResult("unavailable", str(store_path), [], [], "ffmpeg is required for speaker identification.")

    profiles = _load_profiles(store_path)
    candidates: list[tuple[int, list[float], str | None]] = []
    for index, segment in enumerate(diarization.segments):
        if _should_skip_segment(segment):
            continue

        embedding = _extract_segment_embedding(config, session, segment, index)
        if embedding is None:
            continue

        explicit_name = _normalize_explicit_name(segment.speaker)
        candidates.append((index, embedding, explicit_name))

    if not candidates:
        return SpeakerIdentityResult(
            "unavailable",
            str(store_path),
            [],
            profiles,
            "No usable speaker audio segments were available for embedding.",
        )

    clusters = _cluster_candidates(candidates, config.speaker_id_cluster_threshold)
    assignments: list[SpeakerIdentityAssignment] = []
    now = datetime.now().isoformat(timespec="seconds")
    for cluster in clusters:
        profile, similarity = _match_or_create_profile(
            profiles,
            cluster,
            session,
            now,
            config.speaker_id_similarity_threshold,
        )
        assignments.append(
            SpeakerIdentityAssignment(
                speaker_label=profile.display_name,
                profile_id=profile.profile_id,
                similarity=round(similarity, 3),
                session_segment_count=len(cluster.indices),
                aliases=profile.aliases,
            )
        )
        for segment_index in cluster.indices:
            diarization.segments[segment_index].speaker = profile.display_name

    diarization.participants = _merge_participants(
        diarization.participants,
        [segment.speaker for segment in diarization.segments if segment.speaker],
    )
    _save_profiles(store_path, profiles)
    return SpeakerIdentityResult("completed", str(store_path), assignments, profiles)


def identify_live_speaker(
    config: CompanionConfig,
    audio_clip_path: Path,
    *,
    minimum_similarity: float | None = None,
) -> tuple[str, float] | None:
    store_path = config.meetings_root / "speaker-profiles.json"
    if not config.speaker_id_enabled or not audio_clip_path.exists():
        return None

    profiles = _load_profiles(store_path)
    if not profiles:
        return None

    embedding = _compute_embedding(audio_clip_path)
    if embedding is None:
        return None

    threshold = minimum_similarity if minimum_similarity is not None else config.speaker_id_similarity_threshold
    best_profile = None
    best_similarity = -1.0
    for profile in profiles:
        similarity = _cosine_similarity(profile.embedding, embedding)
        if similarity >= threshold and similarity > best_similarity:
            best_profile = profile
            best_similarity = similarity

    if best_profile is None:
        return None

    return best_profile.display_name, round(best_similarity, 3)


def _load_profiles(store_path: Path) -> list[SpeakerProfile]:
    if not store_path.exists():
        return []

    try:
        payload = json.loads(store_path.read_text(encoding="utf-8"))
    except (OSError, ValueError, json.JSONDecodeError):
        return []

    raw_profiles = payload.get("profiles", [])
    profiles: list[SpeakerProfile] = []
    for item in raw_profiles:
        if not isinstance(item, dict):
            continue
        embedding = item.get("embedding", [])
        if not isinstance(embedding, list) or not embedding:
            continue
        profiles.append(
            SpeakerProfile(
                profile_id=str(item.get("profile_id") or item.get("profileId") or item.get("id") or "").strip(),
                display_name=str(item.get("display_name") or item.get("displayName") or "").strip(),
                embedding=[float(value) for value in embedding],
                sample_count=int(item.get("sample_count") or item.get("sampleCount") or 1),
                session_keys=[str(value) for value in item.get("session_keys") or item.get("sessionKeys") or [] if str(value).strip()],
                aliases=[str(value) for value in item.get("aliases", []) if str(value).strip()],
                first_seen=str(item.get("first_seen") or item.get("firstSeen") or "").strip(),
                last_seen=str(item.get("last_seen") or item.get("lastSeen") or "").strip(),
                platforms=[str(value) for value in item.get("platforms", []) if str(value).strip()],
                example_sessions=[str(value) for value in item.get("example_sessions") or item.get("exampleSessions") or [] if str(value).strip()],
            )
        )

    return [profile for profile in profiles if profile.profile_id and profile.display_name]


def _save_profiles(store_path: Path, profiles: list[SpeakerProfile]) -> None:
    store_path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "profiles": [profile.as_dict() for profile in profiles],
        "updatedAt": datetime.now().isoformat(timespec="seconds"),
    }
    store_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def _cluster_candidates(
    candidates: list[tuple[int, list[float], str | None]],
    similarity_threshold: float,
) -> list[_SpeakerCluster]:
    clusters: list[_SpeakerCluster] = []
    for index, embedding, explicit_name in candidates:
        best_cluster = None
        best_similarity = -1.0
        for cluster in clusters:
            similarity = _cosine_similarity(cluster.centroid, embedding)
            if similarity >= similarity_threshold and similarity > best_similarity:
                best_similarity = similarity
                best_cluster = cluster

        if best_cluster is None:
            clusters.append(
                _SpeakerCluster(
                    indices=[index],
                    centroid=embedding[:],
                    explicit_names=[explicit_name] if explicit_name else [],
                )
            )
            continue

        best_cluster.indices.append(index)
        best_cluster.centroid = _blend_embeddings(best_cluster.centroid, embedding, len(best_cluster.indices) - 1, 1)
        if explicit_name:
            best_cluster.explicit_names.append(explicit_name)

    return clusters


def _match_or_create_profile(
    profiles: list[SpeakerProfile],
    cluster: _SpeakerCluster,
    session: FinalizedSession,
    now: str,
    similarity_threshold: float,
) -> tuple[SpeakerProfile, float]:
    best_profile = None
    best_similarity = -1.0
    for profile in profiles:
        similarity = _cosine_similarity(profile.embedding, cluster.centroid)
        if similarity >= similarity_threshold and similarity > best_similarity:
            best_similarity = similarity
            best_profile = profile

    preferred_name = _preferred_explicit_name(cluster.explicit_names)
    if best_profile is None:
        profile = SpeakerProfile(
            profile_id=_next_profile_id(profiles),
            display_name=preferred_name or _VOICE_ID_TEMPLATE.format(number=len(profiles) + 1),
            embedding=cluster.centroid[:],
            sample_count=len(cluster.indices),
            session_keys=[session.session_dir.name],
            aliases=[] if preferred_name is None else [preferred_name],
            first_seen=now,
            last_seen=now,
            platforms=[session.request.platform],
            example_sessions=[session.session_dir.name],
        )
        profiles.append(profile)
        return profile, 1.0

    best_profile.embedding = _blend_embeddings(best_profile.embedding, cluster.centroid, best_profile.sample_count, len(cluster.indices))
    best_profile.sample_count += len(cluster.indices)
    best_profile.last_seen = now
    if session.session_dir.name not in best_profile.session_keys:
        best_profile.session_keys.append(session.session_dir.name)
    if session.request.platform not in best_profile.platforms:
        best_profile.platforms.append(session.request.platform)
    if session.session_dir.name not in best_profile.example_sessions:
        best_profile.example_sessions = [session.session_dir.name, *best_profile.example_sessions][:5]

    if preferred_name:
        if preferred_name not in best_profile.aliases:
            best_profile.aliases.append(preferred_name)
        if _is_generic_label(best_profile.display_name):
            best_profile.display_name = preferred_name

    return best_profile, best_similarity


def _next_profile_id(profiles: list[SpeakerProfile]) -> str:
    max_number = 0
    for profile in profiles:
        tail = profile.profile_id.rsplit("-", 1)[-1]
        if tail.isdigit():
            max_number = max(max_number, int(tail))
    return f"speaker-{max_number + 1:03d}"


def _extract_segment_embedding(
    config: CompanionConfig,
    session: FinalizedSession,
    segment: TranscriptSegment,
    index: int,
) -> list[float] | None:
    source_path = _resolve_audio_source(session, segment)
    if source_path is None or not source_path.exists():
        return None

    duration = max(1.2, min(segment.end - segment.start, 6.0))
    if duration < config.speaker_id_min_segment_seconds:
        return None

    temp_dir = session_artifact_path(session.session_dir, "speaker-id")
    temp_dir.mkdir(parents=True, exist_ok=True)
    clip_path = temp_dir / f"segment-{index:03d}.wav"
    command = [
        config.ffmpeg_path,
        "-y",
        "-ss",
        f"{max(segment.start, 0.0):0.2f}",
        "-i",
        str(source_path),
        "-t",
        f"{duration:0.2f}",
        "-vn",
        "-ac",
        "1",
        "-ar",
        "16000",
        "-f",
        "wav",
        str(clip_path),
    ]

    try:
        subprocess.run(command, check=True, capture_output=True)
        return _compute_embedding(clip_path)
    except Exception:
        return None
    finally:
        try:
            clip_path.unlink(missing_ok=True)
        except OSError:
            pass


def _resolve_audio_source(session: FinalizedSession, segment: TranscriptSegment) -> Path | None:
    if string_equals(segment.source, "microphone") or string_equals(segment.speaker, "You"):
        return session.mic_path if session.mic_path.exists() else None

    if session.system_audio_path and session.system_audio_path.exists():
        return session.system_audio_path

    if session.recording_path.exists():
        return session.recording_path

    return None


def _compute_embedding(path: Path) -> list[float] | None:
    try:
        with wave.open(str(path), "rb") as handle:
            frame_rate = handle.getframerate()
            channels = handle.getnchannels()
            frames = handle.readframes(handle.getnframes())
    except (OSError, wave.Error):
        return None

    if not frames:
        return None

    samples = array("h")
    samples.frombytes(frames)
    if not samples:
        return None

    mono = _to_mono(list(samples), channels)
    if not mono:
        return None

    if len(mono) > 32000:
        step = max(1, len(mono) // 32000)
        mono = mono[::step]
        frame_rate = max(1000, frame_rate // step)

    normalized = [sample / 32768.0 for sample in mono]
    mean_square = sum(sample * sample for sample in normalized) / len(normalized)
    rms = math.sqrt(mean_square)
    if rms <= 0.0005:
        return None

    mean_abs = sum(abs(sample) for sample in normalized) / len(normalized)
    zero_crossings = sum(1 for left, right in zip(normalized, normalized[1:]) if (left < 0) != (right < 0))
    zero_crossing_rate = zero_crossings / max(1, len(normalized) - 1)
    mean_delta = sum(abs(right - left) for left, right in zip(normalized, normalized[1:])) / max(1, len(normalized) - 1)
    peak = max(abs(sample) for sample in normalized)
    std = math.sqrt(sum((sample - 0.0) ** 2 for sample in normalized) / len(normalized))

    frequency_features = _goertzel_features(normalized, frame_rate, [120, 180, 260, 380, 550, 800, 1200, 1800, 2600, 3800])
    pitch_features = _lag_features(normalized, [50, 65, 85, 110, 145, 190, 250, 330])

    vector = [
        mean_abs,
        rms,
        peak,
        std,
        zero_crossing_rate,
        mean_delta,
        *frequency_features,
        *pitch_features,
    ]
    return _normalize_vector(vector)


def _to_mono(samples: list[int], channels: int) -> list[int]:
    if channels <= 1:
        return samples

    mono: list[int] = []
    for index in range(0, len(samples) - channels + 1, channels):
        frame = samples[index:index + channels]
        mono.append(int(sum(frame) / len(frame)))
    return mono


def _goertzel_features(samples: list[float], sample_rate: int, frequencies: list[int]) -> list[float]:
    result: list[float] = []
    window = samples[: min(len(samples), 4096)]
    if not window:
        return [0.0 for _ in frequencies]

    for frequency in frequencies:
        omega = (2.0 * math.pi * frequency) / sample_rate
        coeff = 2.0 * math.cos(omega)
        q0 = 0.0
        q1 = 0.0
        q2 = 0.0
        for sample in window:
            q0 = coeff * q1 - q2 + sample
            q2 = q1
            q1 = q0
        power = q1 * q1 + q2 * q2 - coeff * q1 * q2
        result.append(max(power, 0.0))

    return _normalize_vector(result)


def _lag_features(samples: list[float], lags: list[int]) -> list[float]:
    result: list[float] = []
    if not samples:
        return [0.0 for _ in lags]

    variance = sum(sample * sample for sample in samples)
    if variance <= 0:
        return [0.0 for _ in lags]

    for lag in lags:
        if lag >= len(samples):
            result.append(0.0)
            continue
        numerator = 0.0
        for index in range(len(samples) - lag):
            numerator += samples[index] * samples[index + lag]
        result.append(max(0.0, numerator / variance))

    return result


def _normalize_vector(vector: list[float]) -> list[float]:
    norm = math.sqrt(sum(value * value for value in vector))
    if norm <= 1e-9:
        return [0.0 for _ in vector]
    return [value / norm for value in vector]


def _blend_embeddings(left: list[float], right: list[float], left_weight: int, right_weight: int) -> list[float]:
    total = max(1, left_weight + right_weight)
    blended = [
        ((left_value * left_weight) + (right_value * right_weight)) / total
        for left_value, right_value in zip(left, right, strict=False)
    ]
    if len(left) > len(right):
        blended.extend(left[len(right):])
    elif len(right) > len(left):
        blended.extend(right[len(left):])
    return _normalize_vector(blended)


def _cosine_similarity(left: list[float], right: list[float]) -> float:
    if not left or not right:
        return 0.0
    return sum(left_value * right_value for left_value, right_value in zip(left, right, strict=False))


def _preferred_explicit_name(names: list[str]) -> str | None:
    filtered = [name for name in names if name and not _is_generic_label(name)]
    if not filtered:
        return None
    return Counter(filtered).most_common(1)[0][0]


def _normalize_explicit_name(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = value.strip()
    return normalized or None


def _should_skip_segment(segment: TranscriptSegment) -> bool:
    if segment.end - segment.start < 1.2:
        return True
    if string_equals(segment.speaker, "You"):
        return True
    return False


def _is_generic_label(value: str) -> bool:
    normalized = value.strip().casefold()
    if not normalized:
        return True
    if normalized in _GENERIC_SPEAKER_VALUES:
        return True
    return any(normalized.startswith(prefix) for prefix in _GENERIC_SPEAKER_PREFIXES)


def _merge_participants(existing: list[str], additional: list[str | None]) -> list[str]:
    merged: list[str] = []
    seen: set[str] = set()
    for item in [*existing, *(value for value in additional if value)]:
        normalized = str(item).strip()
        if not normalized:
            continue
        key = normalized.casefold()
        if key in seen:
            continue
        seen.add(key)
        merged.append(normalized)
    return sorted(merged, key=str.casefold)


def string_equals(left: str | None, right: str) -> bool:
    return str(left or "").strip().casefold() == right.casefold()
