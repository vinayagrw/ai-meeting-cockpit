using System.Diagnostics;
using MeetingRecorder.Shared.Configuration;
using MeetingRecorder.Windows.Session;

namespace MeetingRecorder.Windows.Interop;

public sealed class LiveCaptionsLauncher : IDisposable
{
    private Process? _process;

    public void Start(string sessionDirectory)
    {
        Stop();

        var workspaceRoot = RecorderSettingsProvider.WorkspaceRoot;
        var pythonExecutable = RecorderSettingsProvider.ResolvePythonExecutable();
        Directory.CreateDirectory(SessionPathLayout.InternalDirectory(sessionDirectory));
        var captionsLog = SessionPathLayout.InternalArtifact(sessionDirectory, "live-captions-worker.log");
        File.AppendAllText(captionsLog, $"[{DateTimeOffset.Now:O}] Launching live captions worker.{Environment.NewLine}");

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c \"\"{pythonExecutable}\" -m companion.meeting_companion live-captions --session-dir \"{sessionDirectory}\" >> \"{captionsLog}\" 2>&1\"",
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Stop();

}
