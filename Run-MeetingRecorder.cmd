@echo off
setlocal
set "ROOT=%~dp0"

powershell -ExecutionPolicy Bypass -File "%ROOT%scripts\start_windows_recorder_fast.ps1" %*
if errorlevel 1 (
  echo.
  echo Meeting Recorder failed to launch.
  pause
)
