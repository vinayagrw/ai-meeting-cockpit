using MeetingRecorder.CaptureWorker.Services;

var server = new CapturePipeServer();
await server.RunAsync();
