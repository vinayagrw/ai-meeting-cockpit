from __future__ import annotations

from pathlib import Path
import sys


WORKSPACE_ROOT = Path(__file__).resolve().parents[1]
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from companion.meeting_companion.cli import main  # noqa: E402


if __name__ == "__main__":
    raise SystemExit(main())
