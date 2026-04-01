(function attachUtils(root) {
  const constants =
    (typeof module === "object" && module.exports && typeof require === "function"
      ? require("./constants")
      : root.MeetingRecorder.constants);

  function safeUrl(url) {
    try {
      return new URL(url);
    } catch (error) {
      return null;
    }
  }

  function slugify(value) {
    return String(value || "")
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 80) || "meeting";
  }

  function normalizeTitle(value) {
    return String(value || "")
      .replace(/\s+/g, " ")
      .replace(/\|\s*(google meet|microsoft teams|zoom|webex).*$/i, "")
      .trim() || "Untitled meeting";
  }

  function clampText(value, maxLength) {
    const text = String(value || "").replace(/\s+/g, " ").trim();
    return text.length > maxLength ? `${text.slice(0, maxLength - 1)}…` : text;
  }

  function unique(items) {
    return Array.from(new Set(items.filter(Boolean)));
  }

  function getHost(url) {
    const parsed = safeUrl(url);
    return parsed ? parsed.hostname.toLowerCase() : "";
  }

  function hostMatches(host, patterns) {
    return patterns.some((pattern) => pattern.test(host));
  }

  function extractMeetingId(url, platform) {
    const parsed = safeUrl(url);
    if (!parsed) {
      return "unknown";
    }

    const pathParts = parsed.pathname.split("/").filter(Boolean);

    if (platform === "google-meet") {
      return pathParts[0] || "unknown";
    }

    if (platform === "teams-web") {
      const meetupIndex = pathParts.findIndex((part) => part === "meetup-join");
      if (meetupIndex >= 0 && pathParts[meetupIndex + 1]) {
        return decodeURIComponent(pathParts[meetupIndex + 1]);
      }
      return parsed.searchParams.get("meetingId") || parsed.searchParams.get("deeplinkId") || pathParts.at(-1) || "unknown";
    }

    if (platform === "zoom-web") {
      return (
        parsed.searchParams.get("confno") ||
        parsed.searchParams.get("mn") ||
        pathParts.find((part) => /^\d{9,15}$/.test(part)) ||
        pathParts.at(-1) ||
        "unknown"
      );
    }

    if (platform === "webex-web") {
      return parsed.searchParams.get("MTID") || pathParts.at(-1) || "unknown";
    }

    return pathParts.at(-1) || "unknown";
  }

  function makeMeetingKey(platform, meetingId) {
    return `${platform}:${meetingId || "unknown"}`;
  }

  function makeSuppressionExpiry(now) {
    return now + constants.promptSuppressionMs;
  }

  function formatSessionTitle(platform, title, meetingId) {
    const prefix =
      {
        "google-meet": "Google Meet",
        "teams-web": "Teams",
        "zoom-web": "Zoom",
        "webex-web": "Webex"
      }[platform] || "Meeting";

    const normalizedTitle = normalizeTitle(title);
    if (!normalizedTitle || normalizedTitle === "Untitled meeting") {
      return `${prefix} ${meetingId}`;
    }
    return normalizedTitle;
  }

  const api = {
    clampText,
    extractMeetingId,
    formatSessionTitle,
    getHost,
    hostMatches,
    makeMeetingKey,
    makeSuppressionExpiry,
    normalizeTitle,
    safeUrl,
    slugify,
    unique
  };

  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }

  root.MeetingRecorder = root.MeetingRecorder || {};
  root.MeetingRecorder.utils = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
