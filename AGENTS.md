# AGENTS.md

Repo-specific runbook for future Codex work on this project.

The goal of this file is simple: avoid repeating the same launcher, UI, logging, and build mistakes.

## Start Here

- Windows recorder entrypoint is [MeetingDashboardForm.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/MeetingDashboardForm.cs).
- The old split-heavy dashboard in [WidgetForm.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/WidgetForm.cs) became unstable during startup and should not be treated as the active shell surface.
- The shell app is wired from [RecorderApplicationContext.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/RecorderApplicationContext.cs).

## Known Good Launch Paths

- Preferred scripted launch:
  - `.\scripts\start_windows_recorder_fast.ps1`
- Rebuild and relaunch:
  - `.\scripts\start_windows_recorder_fast.ps1 -Rebuild`
- If the launcher looks suspicious, use the direct executable:
  - `Start-Process "C:\Users\viagr\Documents\Codex\windows-shell\MeetingRecorder.Windows\bin\Debug\net10.0-windows\MeetingRecorder.Windows.exe"`

## Build Lock Problem

Symptom:
- `MSB3021`, `MSB3027`, or `apphost.exe` cannot copy because `MeetingRecorder.Windows.exe` is locked.

Cause:
- A running recorder process is still holding the build output open.

Current solution:
- Use [build_windows_shell.ps1](C:/Users/viagr/Documents/Codex/scripts/build_windows_shell.ps1). It now stops repo-local recorder processes first.
- Shared process helpers live in [RecorderProcessTools.ps1](C:/Users/viagr/Documents/Codex/scripts/RecorderProcessTools.ps1).

Do not repeat:
- Do not call raw `dotnet build` against the default `bin\Debug` output while the recorder is still running.

## Launcher Staleness Problem

Symptom:
- The fast launcher rebuilds too often even when no real source files changed.

Cause:
- Generated files like logs or `meetings-data` were being treated as “newer source”.

Current solution:
- [start_windows_recorder_fast.ps1](C:/Users/viagr/Documents/Codex/scripts/start_windows_recorder_fast.ps1) now ignores `bin`, `obj`, `meetings-data`, `logs`, `.tmp`, and temp folders when checking staleness.
- It should also check for an already running recorder before rebuilding.

Do not repeat:
- Do not use generated artifacts as freshness signals for code rebuild decisions.

## Logging Problem

Symptom:
- The app crashes during startup with `UnauthorizedAccessException` on `Documents\Meetings\logs\dotnet-recorder.log`.

Cause:
- Logging to the preferred `Documents\Meetings\logs` path can fail in some environments.

Current solution:
- [ShellDiagnostics.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/ShellDiagnostics.cs) now falls back to a repo-local logs directory under:
  - `windows-shell\MeetingRecorder.Windows\bin\Debug\net10.0-windows\meetings-data\logs`
- Startup exception capture is also wired in [Program.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Program.cs).

Do not repeat:
- Never let diagnostics logging be fatal.
- Any future logging changes must preserve a no-throw fallback path.

## UI Crash Problem

Symptom:
- The app says it launched, but no tray app appears.
- Sometimes a `Microsoft .NET` window appears instead.
- Windows Event Log shows `SplitterDistance` or `GDI+` exceptions.

Cause:
- The prior dashboard used aggressive `SplitContainer` layout and startup-time window forcing.
- That caused startup crashes and `ThreadException` failures around splitter repaint and visibility/layout.

Current solution:
- Use [MeetingDashboardForm.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/MeetingDashboardForm.cs) as the safe dashboard.
- Avoid putting startup-critical UI behind nested `SplitContainer` logic.

Do not repeat:
- Do not make the startup form depend on nested splitters, aggressive `Show()` calls in constructor paths, or re-entrant visibility forcing.
- If a large dashboard redesign is needed, prefer `TableLayoutPanel`, `Panel`, and `TabControl` over stacked split containers.

## Visibility and Tray Debugging

If the user says “the app launched but I can’t see it”:

1. Check whether the process exists:
   - `Get-Process | Where-Object { $_.ProcessName -like 'MeetingRecorder*' }`
2. If it exists, inspect:
   - `MainWindowTitle`
   - `MainWindowHandle`
3. If the title is `Microsoft .NET`, assume an exception dialog is open.
4. Check:
   - repo-local startup error log
   - repo-local recorder log
   - Windows Application Event Log for `.NET Runtime` and `Application Error`

Useful log locations:
- Preferred:
  - `C:\Users\viagr\Documents\Meetings\logs\dotnet-recorder.log`
- Fallback:
  - [dotnet-recorder.log](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/bin/Debug/net10.0-windows/meetings-data/logs/dotnet-recorder.log)
  - [dotnet-startup-trace.log](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/bin/Debug/net10.0-windows/meetings-data/logs/dotnet-startup-trace.log)
  - [dotnet-startup-error.log](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/bin/Debug/net10.0-windows/meetings-data/logs/dotnet-startup-error.log)

## Safe UI Editing Guidance

- Make UI edits in small patches. Large `apply_patch` calls were brittle here.
- If replacing a form, do not delete the old file until the replacement compiles.
- After major WinForms changes, validate with a direct executable launch, not only the PowerShell wrapper.
- When in doubt, compile to an alternate output directory first to validate structure without fighting file locks.

## Current Architecture Notes

- Main shell:
  - [RecorderApplicationContext.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/RecorderApplicationContext.cs)
- Safe dashboard:
  - [MeetingDashboardForm.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/MeetingDashboardForm.cs)
- Legacy dashboard file still present:
  - [WidgetForm.cs](C:/Users/viagr/Documents/Codex/windows-shell/MeetingRecorder.Windows/Shell/WidgetForm.cs)
- Process helpers:
  - [RecorderProcessTools.ps1](C:/Users/viagr/Documents/Codex/scripts/RecorderProcessTools.ps1)
- Build:
  - [build_windows_shell.ps1](C:/Users/viagr/Documents/Codex/scripts/build_windows_shell.ps1)
- Launch:
  - [start_windows_recorder_fast.ps1](C:/Users/viagr/Documents/Codex/scripts/start_windows_recorder_fast.ps1)
  - [run_windows_recorder.ps1](C:/Users/viagr/Documents/Codex/scripts/run_windows_recorder.ps1)

## Secrets and Config

- Do not commit a live API key in [`.env`](C:/Users/viagr/Documents/Codex/.env).
- Use [`.env.example`](C:/Users/viagr/Documents/Codex/.env.example) as the template.
- Runtime behavior belongs in [meeting-recorder.config.json](C:/Users/viagr/Documents/Codex/meeting-recorder.config.json).

## If Something Breaks Again

Use this order:

1. Check whether the process exists.
2. Check fallback logs under `windows-shell\MeetingRecorder.Windows\bin\Debug\net10.0-windows\meetings-data\logs`.
3. Check Windows Application Event Log for `.NET Runtime` and `Application Error`.
4. Validate with direct `.exe` launch before changing the launcher.
5. Prefer the stable dashboard path over another complex splitter-based redesign.
