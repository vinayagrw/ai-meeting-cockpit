(function attachAdapters(root) {
  const utils =
    (typeof module === "object" && module.exports && typeof require === "function"
      ? require("./utils")
      : root.MeetingRecorder.utils);

  const HOST_PATTERNS = {
    "google-meet": [/^meet\.google\.com$/i],
    "teams-web": [/^teams\.microsoft\.com$/i, /^teams\.live\.com$/i],
    "zoom-web": [/^(?:[\w-]+\.)?zoom\.us$/i, /^app\.zoom\.us$/i],
    "webex-web": [/^(?:[\w-]+\.)?webex\.com$/i]
  };

  function collectSelectorHits(documentRef) {
    if (!documentRef || typeof documentRef.querySelector !== "function") {
      return {};
    }

    const selectors = {
      meetLeave: [
        "[aria-label*='Leave call']",
        "[data-tooltip*='Leave call']",
        "[data-call-endpoint]"
      ],
      meetPeople: ["[aria-label*='participants']", "[data-panel-id='5']"],
      teamsLeave: ["[data-tid='call-hangup-button']", "[aria-label*='Leave']"],
      teamsMeetingStage: ["[data-tid='calling-screen']", "[data-tid='toggle-device-settings']"],
      zoomLeave: ["[aria-label*='Leave']", "[data-testid='leave-button']"],
      zoomMeetingStage: ["[data-testid='audio-button']", "[data-testid='video-button']"],
      webexLeave: ["[aria-label*='Leave meeting']", "[aria-label*='End meeting']"],
      webexMeetingStage: ["[data-test='mute-control']", "[data-test='leave-control']"]
    };

    const hits = {};
    Object.keys(selectors).forEach((key) => {
      hits[key] = selectors[key].some((selector) => {
        try {
          return Boolean(documentRef.querySelector(selector));
        } catch (error) {
          return false;
        }
      });
    });
    return hits;
  }

  function collectButtonLabels(documentRef) {
    if (!documentRef || typeof documentRef.querySelectorAll !== "function") {
      return [];
    }

    try {
      return utils.unique(
        Array.from(documentRef.querySelectorAll("button, [role='button'], [aria-label]"))
          .map((node) => {
            const text = (node.textContent || node.innerText || node.getAttribute?.("aria-label") || "")
              .replace(/\s+/g, " ")
              .trim();
            return text;
          })
          .filter(Boolean)
          .slice(0, 80)
      );
    } catch (error) {
      return [];
    }
  }

  function collectMeetingSignals(source, urlOverride) {
    if (source && source.kind === "signals") {
      return source;
    }

    const url = urlOverride || source?.location?.href || "";
    const host = utils.getHost(url);
    return {
      kind: "signals",
      url,
      host,
      title: source?.title || "",
      bodyText: source?.body?.innerText || source?.documentElement?.innerText || "",
      buttonLabels: collectButtonLabels(source),
      selectorHits: collectSelectorHits(source)
    };
  }

  function bodyIncludes(signals, pattern) {
    return pattern.test(signals.bodyText || "");
  }

  function buttonsInclude(signals, pattern) {
    return (signals.buttonLabels || []).some((label) => pattern.test(label));
  }

  function inGoogleMeet(signals) {
    return (
      signals.selectorHits.meetLeave ||
      (buttonsInclude(signals, /leave call|end call/i) && !bodyIncludes(signals, /join now|ask to join/i)) ||
      (signals.selectorHits.meetPeople && bodyIncludes(signals, /you are presenting|everyone/i))
    );
  }

  function inTeams(signals) {
    return (
      signals.selectorHits.teamsLeave ||
      (signals.selectorHits.teamsMeetingStage && buttonsInclude(signals, /leave|camera|microphone/i)) ||
      bodyIncludes(signals, /meeting chat|participants|raise hand/i)
    );
  }

  function inZoom(signals) {
    return (
      signals.selectorHits.zoomLeave ||
      (signals.selectorHits.zoomMeetingStage && buttonsInclude(signals, /leave|mute|start video|stop video/i)) ||
      bodyIncludes(signals, /meeting controls|participants/i)
    );
  }

  function inWebex(signals) {
    return (
      signals.selectorHits.webexLeave ||
      (signals.selectorHits.webexMeetingStage && buttonsInclude(signals, /leave meeting|mute|unmute/i)) ||
      bodyIncludes(signals, /people in this meeting|recording/i)
    );
  }

  function buildMetadata(platform, signals) {
    const meetingId = utils.extractMeetingId(signals.url, platform);
    const title = utils.formatSessionTitle(platform, signals.title, meetingId);
    const participantsVisible =
      buttonsInclude(signals, /participants/i) ||
      signals.selectorHits.meetPeople ||
      signals.selectorHits.teamsMeetingStage ||
      signals.selectorHits.zoomMeetingStage ||
      signals.selectorHits.webexMeetingStage;
    return {
      meetingId,
      participantsHint: participantsVisible ? "participants-visible" : null,
      platform,
      title
    };
  }

  const adapters = [
    {
      id: "google-meet",
      matchUrl(url) {
        return utils.hostMatches(utils.getHost(url), HOST_PATTERNS["google-meet"]);
      },
      isInMeeting(source, urlOverride) {
        return inGoogleMeet(collectMeetingSignals(source, urlOverride));
      },
      getMeetingMeta(source, urlOverride) {
        return buildMetadata(this.id, collectMeetingSignals(source, urlOverride));
      }
    },
    {
      id: "teams-web",
      matchUrl(url) {
        return utils.hostMatches(utils.getHost(url), HOST_PATTERNS["teams-web"]);
      },
      isInMeeting(source, urlOverride) {
        return inTeams(collectMeetingSignals(source, urlOverride));
      },
      getMeetingMeta(source, urlOverride) {
        return buildMetadata(this.id, collectMeetingSignals(source, urlOverride));
      }
    },
    {
      id: "zoom-web",
      matchUrl(url) {
        return utils.hostMatches(utils.getHost(url), HOST_PATTERNS["zoom-web"]);
      },
      isInMeeting(source, urlOverride) {
        return inZoom(collectMeetingSignals(source, urlOverride));
      },
      getMeetingMeta(source, urlOverride) {
        return buildMetadata(this.id, collectMeetingSignals(source, urlOverride));
      }
    },
    {
      id: "webex-web",
      matchUrl(url) {
        return utils.hostMatches(utils.getHost(url), HOST_PATTERNS["webex-web"]);
      },
      isInMeeting(source, urlOverride) {
        return inWebex(collectMeetingSignals(source, urlOverride));
      },
      getMeetingMeta(source, urlOverride) {
        return buildMetadata(this.id, collectMeetingSignals(source, urlOverride));
      }
    }
  ];

  function getMeetingAdapterForUrl(url) {
    return adapters.find((adapter) => adapter.matchUrl(url)) || null;
  }

  function getMeetingMetadata(source, urlOverride) {
    const adapter = getMeetingAdapterForUrl(urlOverride || source?.location?.href || "");
    if (!adapter) {
      return null;
    }
    return adapter.getMeetingMeta(source, urlOverride);
  }

  const api = {
    adapters,
    collectMeetingSignals,
    getMeetingAdapterForUrl,
    getMeetingMetadata
  };

  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }

  root.MeetingRecorder = root.MeetingRecorder || {};
  root.MeetingRecorder.adapters = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
