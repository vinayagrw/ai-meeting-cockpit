from __future__ import annotations

import argparse
import logging

from .config import load_config
from .logging_utils import configure_application_logging
from .pipeline.processor import SessionProcessor
from .server import CompanionServer
from .startup import ensure_startup_registration
from .storage import SessionStorage
from .tray import run_tray

logger = logging.getLogger(__name__)


class CompanionApplication:
    def __init__(self, *, headless: bool, no_startup: bool) -> None:
        self.config = load_config()
        configure_application_logging(self.config, console=True)
        self.storage = SessionStorage(self.config.meetings_root)
        self.processor = SessionProcessor(self.config, self.storage)
        self.server = CompanionServer(self.config, self.storage, self.processor)
        self.headless = headless
        self.no_startup = no_startup
        self._stopped = False

    def start(self) -> None:
        self.server.start()
        logger.info("Meeting companion listening on http://%s:%s", self.config.host, self.server.bound_port)
        if not self.no_startup:
            try:
                ensure_startup_registration(self.config)
            except Exception as error:  # pragma: no cover - Windows-specific best effort
                logger.warning("Unable to register startup shortcut: %s", error)

    def stop(self) -> None:
        if self._stopped:
            return
        self._stopped = True
        logger.info("Stopping meeting companion.")
        self.server.stop()
        self.processor.stop()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run the meeting recorder companion.")
    parser.add_argument("--headless", action="store_true", help="Run without the tray loop.")
    parser.add_argument("--no-startup", action="store_true", help="Do not register startup on login.")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    application = CompanionApplication(headless=bool(args.headless), no_startup=bool(args.no_startup))
    application.start()
    if application.headless:
        try:
            while True:
                import time

                time.sleep(1)
        except KeyboardInterrupt:
            application.stop()
            return 0

    try:
        return run_tray(application)
    finally:
        application.stop()
