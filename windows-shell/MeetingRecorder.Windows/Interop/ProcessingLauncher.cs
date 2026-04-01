using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Session;

namespace MeetingRecorder.Windows.Interop;

public sealed class ProcessingLauncher
{
    public void Launch(string sessionDirectory)
    {
        var workspaceRoot = RecorderSettingsProvider.WorkspaceRoot;
        var pythonExecutable = RecorderSettingsProvider.ResolvePythonExecutable();
        Directory.CreateDirectory(SessionPathLayout.InternalDirectory(sessionDirectory));
        var processingLog = SessionPathLayout.InternalArtifact(sessionDirectory, "processing.log");
        File.AppendAllText(processingLog, $"[{DateTimeOffset.Now:O}] Launching transcript/summary processing.{Environment.NewLine}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c \"\"{pythonExecutable}\" -m companion.meeting_companion process-session --session-dir \"{sessionDirectory}\" >> \"{processingLog}\" 2>&1\"",
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(startInfo);
    }

}
