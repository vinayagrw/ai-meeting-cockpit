(function bootstrapOffscreenDocument() {
  const constants = globalThis.MeetingRecorder.constants;
  const activeSessions = new Map();
  const { messageTypes } = constants;

  function pickSupportedMimeType(candidates) {
    return candidates.find((candidate) => MediaRecorder.isTypeSupported(candidate)) || "";
  }

  async function postJson(url, payload) {
    const response = await fetch(url, {
      body: JSON.stringify(payload),
      headers: {
        "Content-Type": "application/json"
      },
      method: "POST"
    });

    if (!response.ok) {
      throw new Error(`Companion request failed (${response.status})`);
    }
    return response.json().catch(() => ({}));
  }

  async function postChunk(sessionId, track, sequence, blob) {
    const response = await fetch(
      `${constants.companionBaseUrl}/sessions/${encodeURIComponent(sessionId)}/chunk?track=${encodeURIComponent(track)}&sequence=${encodeURIComponent(sequence)}`,
      {
        body: await blob.arrayBuffer(),
        headers: {
          "Content-Type": blob.type || "application/octet-stream"
        },
        method: "POST"
      }
    );

    if (!response.ok) {
      throw new Error(`Chunk upload failed (${response.status})`);
    }
  }

  async function getTabStream(streamId) {
    return navigator.mediaDevices.getUserMedia({
      audio: {
        mandatory: {
          chromeMediaSource: "tab",
          chromeMediaSourceId: streamId
        }
      },
      video: {
        mandatory: {
          chromeMediaSource: "tab",
          chromeMediaSourceId: streamId,
          maxFrameRate: 30,
          maxHeight: 2160,
          maxWidth: 3840
        }
      }
    });
  }

  async function getMicStream() {
    return navigator.mediaDevices.getUserMedia({
      audio: {
        autoGainControl: false,
        echoCancellation: false,
        noiseSuppression: false
      }
    });
  }

  function createMixedStream(tabStream, micStream) {
    const audioContext = new AudioContext();
    const destination = audioContext.createMediaStreamDestination();
    const tabSource = audioContext.createMediaStreamSource(tabStream);
    const micSource = audioContext.createMediaStreamSource(micStream);
    tabSource.connect(destination);
    micSource.connect(destination);

    const mixedStream = new MediaStream([
      ...tabStream.getVideoTracks(),
      ...destination.stream.getAudioTracks()
    ]);

    return {
      audioContext,
      mixedStream
    };
  }

  function waitForStop(recorder) {
    return new Promise((resolve) => {
      if (recorder.state === "inactive") {
        resolve();
        return;
      }

      recorder.addEventListener(
        "stop",
        () => {
          resolve();
        },
        { once: true }
      );

      recorder.stop();
    });
  }

  function attachChunkUploader(sessionState, recorder, track) {
    recorder.addEventListener("dataavailable", (event) => {
      if (!event.data || event.data.size === 0) {
        return;
      }

      const sequence = sessionState.nextSequence[track];
      sessionState.nextSequence[track] += 1;
      sessionState.uploadChain = sessionState.uploadChain.then(() =>
        postChunk(sessionState.session.sessionId, track, sequence, event.data)
      );
    });
  }

  async function startOffscreenRecording(request) {
    if (activeSessions.has(request.session.sessionId)) {
      return { ok: true };
    }

    await postJson(`${constants.companionBaseUrl}/sessions/start`, request.session);

    const tabStream = await getTabStream(request.streamId);
    const micStream = await getMicStream();
    const { audioContext, mixedStream } = createMixedStream(tabStream, micStream);

    const mainRecorder = new MediaRecorder(
      mixedStream,
      pickSupportedMimeType([
        "video/webm;codecs=vp9,opus",
        "video/webm;codecs=vp8,opus",
        "video/webm"
      ])
        ? {
            mimeType: pickSupportedMimeType([
              "video/webm;codecs=vp9,opus",
              "video/webm;codecs=vp8,opus",
              "video/webm"
            ])
          }
        : undefined
    );

    const micRecorder = new MediaRecorder(
      new MediaStream(micStream.getAudioTracks()),
      pickSupportedMimeType(["audio/webm;codecs=opus", "audio/webm"])
        ? {
            mimeType: pickSupportedMimeType(["audio/webm;codecs=opus", "audio/webm"])
          }
        : undefined
    );

    const sessionState = {
      audioContext,
      mainRecorder,
      micRecorder,
      micStream,
      mixedStream,
      nextSequence: {
        main: 0,
        mic: 0
      },
      session: request.session,
      stopping: false,
      streams: [tabStream, micStream],
      uploadChain: Promise.resolve()
    };

    attachChunkUploader(sessionState, mainRecorder, "main");
    attachChunkUploader(sessionState, micRecorder, "mic");

    activeSessions.set(request.session.sessionId, sessionState);
    mainRecorder.start(request.session.chunkIntervalMs || constants.chunkIntervalMs);
    micRecorder.start(request.session.chunkIntervalMs || constants.chunkIntervalMs);
    return { ok: true };
  }

  async function finalizeSession(sessionId, reason) {
    const sessionState = activeSessions.get(sessionId);
    if (!sessionState || sessionState.stopping) {
      return { ok: true };
    }

    sessionState.stopping = true;
    await Promise.all([waitForStop(sessionState.mainRecorder), waitForStop(sessionState.micRecorder)]);
    await sessionState.uploadChain.catch((error) => {
      console.error("Chunk upload failed", error);
    });

    sessionState.streams.forEach((stream) => {
      stream.getTracks().forEach((track) => track.stop());
    });
    sessionState.audioContext.close().catch(() => null);

    let stopError = null;
    try {
      await postJson(`${constants.companionBaseUrl}/sessions/${encodeURIComponent(sessionId)}/stop`, {
        reason,
        stoppedAt: new Date().toISOString()
      });
    } catch (error) {
      stopError = error;
      console.error("Unable to finalize companion session", error);
    }

    activeSessions.delete(sessionId);
    await chrome.runtime.sendMessage({
      error: stopError ? stopError.message : null,
      reason,
      sessionId,
      type: messageTypes.offscreenSessionStopped
    });
    return { ok: true };
  }

  chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.type === messageTypes.startOffscreenRecording) {
      startOffscreenRecording(request)
        .then((value) => sendResponse(value))
        .catch((error) => {
          console.error("Unable to start recording", error);
          sendResponse({ error: error.message || "Unable to start recording", ok: false });
        });
      return true;
    }

    if (request.type === messageTypes.stopOffscreenRecording) {
      finalizeSession(request.sessionId, request.reason || "user-stop")
        .then((value) => sendResponse(value))
        .catch((error) => {
          console.error("Unable to stop recording", error);
          sendResponse({ error: error.message || "Unable to stop recording", ok: false });
        });
      return true;
    }

    return false;
  });
})();
