(function bootstrapContentScript() {
  const shared = globalThis.MeetingRecorder || {};
  const constants = shared.constants;
  const utils = shared.utils;
  const adaptersApi = shared.adapters;

  if (!constants || !utils || !adaptersApi) {
    return;
  }

  const { messageTypes } = constants;
  const state = {
    activeSession: null,
    captionSyncInFlight: false,
    inactiveSince: null,
    lastCaptionFingerprint: null,
    lastCaptionSentAt: 0,
    promptCheckInFlight: false,
    promptSince: null,
    renderedMeetingKey: null
  };

  const ui = createUi();

  function createUi() {
    const root = document.createElement("div");
    root.id = "meeting-recorder-root";
    root.innerHTML = [
      '<section class="meeting-recorder-card" data-state="hidden">',
      '  <div class="meeting-recorder-eyebrow">Meeting Recorder</div>',
      '  <h2 class="meeting-recorder-title"></h2>',
      '  <p class="meeting-recorder-copy"></p>',
      '  <div class="meeting-recorder-actions">',
      '    <button class="meeting-recorder-button meeting-recorder-button--primary" data-action="primary"></button>',
      '    <button class="meeting-recorder-button meeting-recorder-button--secondary" data-action="secondary"></button>',
      "  </div>",
      '  <div class="meeting-recorder-status"></div>',
      "</section>"
    ].join("");
    document.documentElement.appendChild(root);

    const card = root.querySelector(".meeting-recorder-card");
    const title = root.querySelector(".meeting-recorder-title");
    const copy = root.querySelector(".meeting-recorder-copy");
    const primary = root.querySelector("[data-action='primary']");
    const secondary = root.querySelector("[data-action='secondary']");
    const status = root.querySelector(".meeting-recorder-status");

    primary.addEventListener("click", () => {
      if (card.dataset.state === "prompt") {
        void startRecording();
      } else if (card.dataset.state === "recording") {
        void stopRecording("user-stop");
      }
    });

    secondary.addEventListener("click", () => {
      if (card.dataset.state === "prompt") {
        void dismissPrompt();
      } else if (card.dataset.state === "recording") {
        hideBanner();
      }
    });

    function renderPrompt(meeting) {
      card.dataset.state = "prompt";
      title.textContent = meeting.title;
      copy.textContent = "Start screen, tab audio, and microphone capture in the background for this meeting.";
      primary.textContent = "Start in background";
      secondary.textContent = "Not now";
      status.textContent = "Nothing is recorded until you click Start.";
    }

    function renderRecording(session) {
      card.dataset.state = "recording";
      title.textContent = session.title;
      copy.textContent = "Recording is running in the background. Chunks are streaming to your local Windows companion.";
      primary.textContent = "Stop recording";
      secondary.textContent = "Hide";
      status.textContent = `Session ${session.sessionId.slice(0, 8)} is active.`;
    }

    function renderError(message) {
      card.dataset.state = "prompt";
      title.textContent = "Companion unavailable";
      copy.textContent = message;
      primary.textContent = "Try again";
      secondary.textContent = "Dismiss";
      status.textContent = "Make sure the local companion app is running on 127.0.0.1:17823.";
    }

    function hide() {
      card.dataset.state = "hidden";
    }

    return {
      hide,
      hideBanner: hide,
      renderError,
      renderPrompt,
      renderRecording
    };
  }

  function hideBanner() {
    if (!state.activeSession) {
      ui.hide();
    }
  }

  async function sendMessage(message) {
    return chrome.runtime.sendMessage(message);
  }

  function normalizeText(value) {
    return String(value || "")
      .replace(/\u00a0/g, " ")
      .replace(/\s+/g, " ")
      .trim();
  }

  function isVisibleElement(element) {
    if (!element || typeof element.getBoundingClientRect !== "function") {
      return false;
    }

    const style = window.getComputedStyle(element);
    if (style.display === "none" || style.visibility === "hidden") {
      return false;
    }

    const rect = element.getBoundingClientRect();
    return rect.width > 1 && rect.height > 1;
  }

  function readCaptionParts(node) {
    if (!node || typeof node.querySelectorAll !== "function") {
      return [];
    }

    const childTexts = Array.from(node.children || [])
      .map((child) => normalizeText(child.innerText || child.textContent || ""))
      .filter(Boolean);

    if (childTexts.length >= 2) {
      return utils.unique(childTexts.slice(0, 4));
    }

    const blockText = String(node.innerText || node.textContent || "");
    const lines = blockText
      .split(/\r?\n/)
      .map((line) => normalizeText(line))
      .filter(Boolean);

    return utils.unique(lines.slice(0, 4));
  }

  function looksLikeCaption(parts) {
    if (!Array.isArray(parts) || parts.length < 2) {
      return false;
    }

    const speaker = parts[0];
    const text = parts.slice(1).join(" ");
    if (!speaker || !text) {
      return false;
    }

    if (speaker.length > 80 || text.length > 240) {
      return false;
    }

    return !/turn on captions|captions are unavailable|subtitles/i.test(text);
  }

  function collectGoogleMeetCaptions() {
    const liveRegions = Array.from(document.querySelectorAll("[aria-live='polite'], [aria-live='assertive']"))
      .filter(isVisibleElement);
    const entries = [];
    const seen = new Set();

    liveRegions.forEach((region) => {
      const candidates = [region].concat(Array.from(region.querySelectorAll("div, li, section, [role='listitem']"))).slice(0, 240);
      candidates.forEach((candidate) => {
        if (!isVisibleElement(candidate)) {
          return;
        }

        const parts = readCaptionParts(candidate);
        if (!looksLikeCaption(parts)) {
          return;
        }

        const speaker = parts[0];
        const text = parts.slice(1).join(" ").trim();
        const captionId = `${speaker}|${text}`;
        if (seen.has(captionId)) {
          return;
        }

        seen.add(captionId);
        entries.push({
          captionId,
          observedAt: new Date().toISOString(),
          speaker,
          text
        });
      });
    });

    return entries.slice(-8);
  }

  function collectBrowserCaptionPayload(context) {
    if (!context?.meeting || context.meeting.platform !== "google-meet") {
      return null;
    }

    const captions = collectGoogleMeetCaptions();
    const participants = utils.unique(
      captions
        .map((caption) => caption.speaker)
        .filter(Boolean)
    );

    return {
      captions,
      meetingId: context.meeting.meetingId,
      participants,
      platform: context.meeting.platform,
      sourceUrl: window.location.href,
      title: context.meeting.title,
      windowTitle: document.title
    };
  }

  async function syncBrowserCaptions(context) {
    const payload = collectBrowserCaptionPayload(context);
    if (!payload || state.captionSyncInFlight) {
      return;
    }

    const fingerprint = JSON.stringify({
      captions: payload.captions.map((entry) => `${entry.speaker}|${entry.text}`),
      participants: payload.participants
    });
    const now = Date.now();
    if (fingerprint === state.lastCaptionFingerprint && now - state.lastCaptionSentAt < 5000) {
      return;
    }

    state.captionSyncInFlight = true;
    try {
      await fetch(`${constants.browserCaptionBridgeUrl}/browser-captions`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify(payload),
        keepalive: true
      });
      state.lastCaptionFingerprint = fingerprint;
      state.lastCaptionSentAt = now;
    } catch (error) {
      // Ignore bridge errors so the meeting UI remains unobtrusive.
    } finally {
      state.captionSyncInFlight = false;
    }
  }

  function getCurrentMeetingContext() {
    const adapter = adaptersApi.getMeetingAdapterForUrl(window.location.href);
    if (!adapter) {
      return null;
    }

    return {
      adapter,
      inMeeting: adapter.isInMeeting(document, window.location.href),
      meeting: adapter.getMeetingMeta(document, window.location.href)
    };
  }

  async function dismissPrompt() {
    const context = getCurrentMeetingContext();
    if (context?.meeting) {
      await sendMessage({
        meetingKey: utils.makeMeetingKey(context.meeting.platform, context.meeting.meetingId),
        type: messageTypes.dismissPrompt
      });
    }
    state.renderedMeetingKey = null;
    ui.hide();
  }

  async function startRecording() {
    const context = getCurrentMeetingContext();
    if (!context?.meeting) {
      return;
    }

    const response = await sendMessage({
      meeting: context.meeting,
      sourceUrl: window.location.href,
      type: messageTypes.startRecording
    });

    if (!response?.ok) {
      ui.renderError(response?.error || "Unable to start the session.");
      return;
    }

    state.activeSession = response.session;
    state.inactiveSince = null;
    state.renderedMeetingKey = utils.makeMeetingKey(context.meeting.platform, context.meeting.meetingId);
    ui.renderRecording(response.session);
  }

  async function stopRecording(reason) {
    if (!state.activeSession) {
      return;
    }

    await sendMessage({
      reason,
      sessionId: state.activeSession.sessionId,
      type: messageTypes.stopRecording
    });
  }

  async function maybePrompt(meeting) {
    if (state.promptCheckInFlight) {
      return;
    }

    const meetingKey = utils.makeMeetingKey(meeting.platform, meeting.meetingId);
    if (state.renderedMeetingKey === meetingKey || state.activeSession) {
      return;
    }

    state.promptCheckInFlight = true;
    try {
      const response = await sendMessage({
        meeting,
        meetingKey,
        type: messageTypes.shouldPrompt
      });

      if (response?.shouldPrompt) {
        state.renderedMeetingKey = meetingKey;
        ui.renderPrompt(meeting);
      }
    } finally {
      state.promptCheckInFlight = false;
    }
  }

  async function syncTabState() {
    const response = await sendMessage({ type: messageTypes.syncTabState });
    if (response?.session) {
      state.activeSession = response.session;
      ui.renderRecording(response.session);
    }
  }

  async function evaluateMeetingState() {
    const context = getCurrentMeetingContext();
    if (!context) {
      if (!state.activeSession) {
        ui.hide();
      }
      return;
    }

    const meetingKey = utils.makeMeetingKey(context.meeting.platform, context.meeting.meetingId);
    if (!context.inMeeting) {
      state.promptSince = null;
      state.renderedMeetingKey = null;
      state.lastCaptionFingerprint = null;
      if (state.activeSession) {
        state.inactiveSince = state.inactiveSince || Date.now();
        if (Date.now() - state.inactiveSince >= constants.inactiveStopAfterMs) {
          await stopRecording("meeting-ended");
        }
      } else {
        ui.hide();
      }
      return;
    }

    state.inactiveSince = null;
    await syncBrowserCaptions(context);
    if (state.activeSession) {
      ui.renderRecording(state.activeSession);
      return;
    }

    if (state.renderedMeetingKey && state.renderedMeetingKey !== meetingKey) {
      state.renderedMeetingKey = null;
      state.promptSince = Date.now();
      ui.hide();
    } else {
      state.promptSince = state.promptSince || Date.now();
    }

    if (Date.now() - state.promptSince >= constants.promptAfterMs) {
      await maybePrompt(context.meeting);
    }
  }

  chrome.runtime.onMessage.addListener((message) => {
    if (message.type === messageTypes.sessionStarted) {
      state.activeSession = message.session;
      ui.renderRecording(message.session);
    }

    if (message.type === messageTypes.sessionStopped) {
      if (!state.activeSession || state.activeSession.sessionId === message.sessionId) {
        state.activeSession = null;
        state.inactiveSince = null;
        state.promptSince = null;
        state.renderedMeetingKey = null;
        ui.hide();
      }
    }
  });

  const observer = new MutationObserver(() => {
    void evaluateMeetingState();
  });

  observer.observe(document.documentElement, {
    childList: true,
    subtree: true
  });

  void syncTabState();
  void evaluateMeetingState();
  window.setInterval(() => {
    void evaluateMeetingState();
  }, constants.pollIntervalMs);
})();
