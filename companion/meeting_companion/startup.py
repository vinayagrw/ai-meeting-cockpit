from __future__ import annotations

from pathlib import Path
import os
import sys

from .config import CompanionConfig

if os.name == "nt":  # pragma: no cover - Windows specific
    import winreg


def _startup_command(script_path: Path) -> str:
    return f'"{sys.executable}" "{script_path}"'


def ensure_startup_registration(config: CompanionConfig) -> bool:
    if os.name != "nt":  # pragma: no cover - Windows specific
        return False

    script_path = config.workspace_root / "scripts" / "run_companion.py"
    if not script_path.exists():
        return False

    command = _startup_command(script_path)
    with winreg.OpenKey(
        winreg.HKEY_CURRENT_USER,
        r"Software\Microsoft\Windows\CurrentVersion\Run",
        0,
        winreg.KEY_SET_VALUE,
    ) as key:
        winreg.SetValueEx(key, config.startup_name, 0, winreg.REG_SZ, command)
    return True
