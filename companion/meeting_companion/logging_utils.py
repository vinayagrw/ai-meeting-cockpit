from __future__ import annotations

import logging
from logging.handlers import RotatingFileHandler
from pathlib import Path

from .config import CompanionConfig


def python_app_log_path(config: CompanionConfig) -> Path:
    return config.app_logs_root / "python-companion.log"


def configure_application_logging(config: CompanionConfig, *, console: bool = True) -> Path:
    log_path = python_app_log_path(config)
    log_path.parent.mkdir(parents=True, exist_ok=True)

    root_logger = logging.getLogger()
    root_logger.setLevel(logging.INFO)

    formatter = logging.Formatter("%(asctime)s %(levelname)s %(name)s %(message)s")

    file_handler_exists = any(
        isinstance(handler, RotatingFileHandler) and Path(getattr(handler, "baseFilename", "")) == log_path
        for handler in root_logger.handlers
    )
    if not file_handler_exists:
        file_handler = RotatingFileHandler(
            log_path,
            maxBytes=1_048_576,
            backupCount=5,
            encoding="utf-8",
        )
        file_handler.setFormatter(formatter)
        root_logger.addHandler(file_handler)

    if console:
        console_handler_exists = any(
            isinstance(handler, logging.StreamHandler) and not isinstance(handler, RotatingFileHandler)
            for handler in root_logger.handlers
        )
        if not console_handler_exists:
            console_handler = logging.StreamHandler()
            console_handler.setFormatter(formatter)
            root_logger.addHandler(console_handler)

    logging.getLogger(__name__).info("Python app logging ready at %s", log_path)
    return log_path
