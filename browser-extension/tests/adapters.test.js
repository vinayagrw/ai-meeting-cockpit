const assert = require("node:assert/strict");

const adaptersApi = require("../src/shared/adapters");
const utils = require("../src/shared/utils");

function run(name, fn) {
  try {
    fn();
    console.log(`ok - ${name}`);
  } catch (error) {
    console.error(`not ok - ${name}`);
    throw error;
  }
}

run("detects an active Google Meet session", () => {
  const adapter = adaptersApi.getMeetingAdapterForUrl("https://meet.google.com/abc-defg-hij");
  const signals = {
    kind: "signals",
    url: "https://meet.google.com/abc-defg-hij",
    title: "Weekly Standup | Google Meet",
    bodyText: "Everyone You are presenting",
    buttonLabels: ["Leave call", "Present now"],
    selectorHits: {
      meetLeave: true,
      meetPeople: true
    }
  };

  assert.ok(adapter);
  assert.equal(adapter.id, "google-meet");
  assert.equal(adapter.isInMeeting(signals), true);
  assert.deepEqual(adapter.getMeetingMeta(signals), {
    meetingId: "abc-defg-hij",
    participantsHint: "participants-visible",
    platform: "google-meet",
    title: "Weekly Standup"
  });
});

run("does not treat a Google Meet pre-join screen as active", () => {
  const adapter = adaptersApi.getMeetingAdapterForUrl("https://meet.google.com/abc-defg-hij");
  const signals = {
    kind: "signals",
    url: "https://meet.google.com/abc-defg-hij",
    title: "Join meeting | Google Meet",
    bodyText: "Join now Ask to join",
    buttonLabels: ["Join now", "Present"],
    selectorHits: {
      meetLeave: false,
      meetPeople: false
    }
  };

  assert.ok(adapter);
  assert.equal(adapter.isInMeeting(signals), false);
});

run("builds stable meeting keys", () => {
  assert.equal(utils.makeMeetingKey("zoom-web", "123456789"), "zoom-web:123456789");
});
