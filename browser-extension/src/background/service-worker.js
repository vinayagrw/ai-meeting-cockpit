importScripts("../shared/constants.js", "../shared/utils.js", "../shared/adapters.js");

const constants = self.MeetingRecorder.constants;
const utils = self.MeetingRecorder.utils;

const { messageTypes, storageKeys } = constants;

async function getStorageState() {
  const state = await chrome.storage.local.get([
    storageKeys.activeSessions,
    storageKeys.promptSuppressions,
    storageKeys.tabSessions
  ]);

  return {
    activeSessions: state[storageKeys.activeSessions] || {},
    promptSuppressions: state[storageKeys.promptSuppressions] || {},
    tabSessions: state[storageKeys.tabSessions] || {}
  };
}

async function patchStorage(partialState) {
  await chrome.storage.local.set(partialState);
}

async function ensureStorageDefaults() {
  const state = await getStorageState();
  await patchStorage({
    [storageKeys.activeSessions]: state.activeSessions,
    [storageKeys.promptSuppressions]: state.promptSuppressions,
    [storageKeys.tabSessions]: state.tabSessions
  });
}

async function hasOffscreenDocument() {
  if (chrome.offscreen && typeof chrome.offscreen.hasDocument === "function") {
    return chrome.offscreen.hasDocument();
  }

  const contexts = await chrome.runtime.getContexts({
    contextTypes: ["OFFSCREEN_DOCUMENT"],
    documentUrls: [chrome.runtime.getURL("src/offscreen/offscreen.html")]
  });
  return contexts.length > 0;
}

async function ensureOffscreenDocument() {
  if (await hasOffscreenDocument()) {
    return;
  }

  await chrome.offscreen.createDocument({
    justification: "Capture meeting media in the background while streaming chunks to the local companion.",
    reasons: ["USER_MEDIA", "DISPLAY_MEDIA"],
    url: "src/offscreen/offscreen.html"
  });
}

async function safeSendTabMessage(tabId, message) {
  if (typeof tabId !== "number") {
    return;
  }

  try {
    await chrome.tabs.sendMessage(tabId, message);
  } catch (error) {
    console.warn("Unable to send tab message", error);
  }
}

async function storePromptSuppression(meetingKey) {
  const state = await getStorageState();
  state.promptSuppressions[meetingKey] = utils.makeSuppressionExpiry(Date.now());
  await patchStorage({
    [storageKeys.promptSuppressions]: state.promptSuppressions
  });
}

async function shouldPrompt(meetingKey) {
  const state = await getStorageState();
  const expiry = state.promptSuppressions[meetingKey] || 0;
  return Date.now() > expiry;
}

async function persistSession(session) {
  const state = await getStorageState();
  state.activeSessions[session.sessionId] = session;
  state.tabSessions[String(session.tabId)] = session.sessionId;
  await patchStorage({
    [storageKeys.activeSessions]: state.activeSessions,
    [storageKeys.tabSessions]: state.tabSessions
  });
}

async function getSessionForTab(tabId) {
  const state = await getStorageState();
  const sessionId = state.tabSessions[String(tabId)];
  return sessionId ? state.activeSessions[sessionId] : null;
}

async function removeSession(sessionId) {
  const state = await getStorageState();
  const session = state.activeSessions[sessionId];
  if (!session) {
    return null;
  }

  delete state.activeSessions[sessionId];
  delete state.tabSessions[String(session.tabId)];
  await patchStorage({
    [storageKeys.activeSessions]: state.activeSessions,
    [storageKeys.tabSessions]: state.tabSessions
  });
  return session;
}

async function startRecording(request, sender) {
  const tabId = sender.tab?.id;
  if (typeof tabId !== "number") {
    return { error: "Recording requires an active meeting tab.", ok: false };
  }

  const streamId = await chrome.tabCapture.getMediaStreamId({ targetTabId: tabId });
  const session = {
    ...request.meeting,
    browserTabId: tabId,
    chunkIntervalMs: constants.chunkIntervalMs,
    sessionId: crypto.randomUUID(),
    sourceUrl: request.sourceUrl || sender.tab.url || "",
    startedAt: new Date().toISOString(),
    tabId
  };

  await ensureOffscreenDocument();
  const response = await chrome.runtime.sendMessage({
    streamId,
    session,
    type: messageTypes.startOffscreenRecording
  });

  if (!response?.ok) {
    return response || { error: "Unable to start offscreen capture.", ok: false };
  }

  await persistSession(session);
  await storePromptSuppression(utils.makeMeetingKey(session.platform, session.meetingId));
  await safeSendTabMessage(tabId, { session, type: messageTypes.sessionStarted });
  return { ok: true, session };
}

async function requestStop(sessionId, reason) {
  if (!sessionId) {
    return { ok: true };
  }

  await chrome.runtime.sendMessage({
    reason,
    sessionId,
    type: messageTypes.stopOffscreenRecording
  });

  return { ok: true };
}

async function stopRecording(request, sender) {
  const sessionId =
    request.sessionId ||
    (typeof sender.tab?.id === "number" ? (await getSessionForTab(sender.tab.id))?.sessionId : null);

  if (!sessionId) {
    return { ok: true };
  }

  return requestStop(sessionId, request.reason || "user-stop");
}

async function syncTabState(sender) {
  const tabId = sender.tab?.id;
  if (typeof tabId !== "number") {
    return { session: null };
  }

  return {
    session: await getSessionForTab(tabId)
  };
}

async function handleOffscreenStopped(request) {
  const session = await removeSession(request.sessionId);
  if (session) {
    await safeSendTabMessage(session.tabId, {
      reason: request.reason,
      sessionId: request.sessionId,
      type: messageTypes.sessionStopped
    });
  }
  return { ok: true };
}

chrome.runtime.onInstalled.addListener(() => {
  void ensureStorageDefaults();
});

chrome.runtime.onStartup.addListener(() => {
  void ensureStorageDefaults();
});

chrome.tabs.onRemoved.addListener((tabId) => {
  void getSessionForTab(tabId).then((session) => {
    if (session) {
      return requestStop(session.sessionId, "tab-closed");
    }
    return null;
  });
});

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  const respond = (promise) => {
    promise
      .then((value) => sendResponse(value))
      .catch((error) => {
        console.error("Background message failed", error);
        sendResponse({ error: error.message || "Unexpected error", ok: false });
      });
  };

  if (request.type === messageTypes.shouldPrompt) {
    respond(
      shouldPrompt(request.meetingKey).then((result) => ({
        shouldPrompt: result
      }))
    );
    return true;
  }

  if (request.type === messageTypes.dismissPrompt) {
    respond(storePromptSuppression(request.meetingKey).then(() => ({ ok: true })));
    return true;
  }

  if (request.type === messageTypes.startRecording) {
    respond(startRecording(request, sender));
    return true;
  }

  if (request.type === messageTypes.stopRecording) {
    respond(stopRecording(request, sender));
    return true;
  }

  if (request.type === messageTypes.syncTabState) {
    respond(syncTabState(sender));
    return true;
  }

  if (request.type === messageTypes.offscreenSessionStopped) {
    respond(handleOffscreenStopped(request));
    return true;
  }

  return false;
});
