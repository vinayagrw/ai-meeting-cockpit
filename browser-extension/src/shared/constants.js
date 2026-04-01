(function attachConstants(root) {
  const constants = {
    extensionName: "Meeting Recorder",
    companionBaseUrl: "http://127.0.0.1:17823",
    browserCaptionBridgeUrl: "http://127.0.0.1:17824",
    pollIntervalMs: 1000,
    promptAfterMs: 5000,
    inactiveStopAfterMs: 20000,
    promptSuppressionMs: 30 * 60 * 1000,
    chunkIntervalMs: 20000,
    storageKeys: {
      promptSuppressions: "promptSuppressions",
      activeSessions: "activeSessions",
      tabSessions: "tabSessions"
    },
    messageTypes: {
      dismissPrompt: "DISMISS_PROMPT",
      offscreenSessionStopped: "OFFSCREEN_SESSION_STOPPED",
      sessionStarted: "SESSION_STARTED",
      sessionStopped: "SESSION_STOPPED",
      shouldPrompt: "SHOULD_PROMPT",
      startOffscreenRecording: "START_OFFSCREEN_RECORDING",
      startRecording: "START_RECORDING",
      stopOffscreenRecording: "STOP_OFFSCREEN_RECORDING",
      stopRecording: "STOP_RECORDING",
      syncTabState: "SYNC_TAB_STATE"
    }
  };

  if (typeof module === "object" && module.exports) {
    module.exports = constants;
  }

  root.MeetingRecorder = root.MeetingRecorder || {};
  root.MeetingRecorder.constants = constants;
})(typeof globalThis !== "undefined" ? globalThis : this);
