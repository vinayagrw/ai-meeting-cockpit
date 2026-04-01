from __future__ import annotations

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import logging
from threading import Thread
from urllib.parse import parse_qs, unquote, urlparse

from .config import CompanionConfig
from .models import SessionStartRequest, StopRequest
from .pipeline.processor import SessionProcessor
from .storage import SessionStorage

logger = logging.getLogger(__name__)


class ReusableThreadingHTTPServer(ThreadingHTTPServer):
    allow_reuse_address = True


class CompanionRequestHandler(BaseHTTPRequestHandler):
    config: CompanionConfig
    storage: SessionStorage
    processor: SessionProcessor

    def log_message(self, format: str, *args) -> None:  # noqa: A003
        logger.info("HTTP %s - %s", self.address_string(), format % args)

    def _read_json_body(self) -> dict:
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        return json.loads(raw.decode("utf-8"))

    def _read_bytes_body(self) -> bytes:
        length = int(self.headers.get("Content-Length", "0"))
        return self.rfile.read(length) if length else b""

    def _send_json(self, status_code: int, payload: dict) -> None:
        encoded = json.dumps(payload).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        parsed = urlparse(self.path)
        if parsed.path == "/health":
            self._send_json(
                200,
                {
                    "ok": True,
                    "activeSessions": self.storage.active_count,
                    "queuedSessions": self.processor.pending_count(),
                },
            )
            return

        self._send_json(404, {"error": "Not found", "ok": False})
        logger.warning("Unhandled GET path: %s", parsed.path)

    def do_POST(self) -> None:  # noqa: N802
        try:
            parsed = urlparse(self.path)
            if parsed.path == "/sessions/start":
                payload = self._read_json_body()
                request_payload = SessionStartRequest.from_payload(payload)
                session = self.storage.start_session(request_payload)
                logger.info("Started session %s in %s", request_payload.session_id, session.session_dir)
                self._send_json(
                    201,
                    {
                        "folder": str(session.session_dir),
                        "ok": True,
                        "sessionId": request_payload.session_id,
                    },
                )
                return

            path_parts = [part for part in parsed.path.split("/") if part]
            if len(path_parts) == 3 and path_parts[0] == "sessions" and path_parts[2] == "chunk":
                session_id = unquote(path_parts[1])
                query = parse_qs(parsed.query)
                track = query.get("track", ["main"])[0]
                sequence = int(query.get("sequence", ["0"])[0])
                self.storage.append_chunk(session_id, track, sequence, self._read_bytes_body())
                logger.info("Received %s chunk #%s for %s", track, sequence, session_id)
                self._send_json(202, {"ok": True})
                return

            if len(path_parts) == 3 and path_parts[0] == "sessions" and path_parts[2] == "stop":
                session_id = unquote(path_parts[1])
                stop_request = StopRequest.from_payload(self._read_json_body())
                session = self.storage.stop_session(session_id, stop_request)
                self.processor.enqueue(session)
                logger.info("Stopped session %s (%s)", session_id, stop_request.reason)
                self._send_json(202, {"ok": True, "sessionId": session_id})
                return

            self._send_json(404, {"error": "Not found", "ok": False})
            logger.warning("Unhandled POST path: %s", parsed.path)
        except ValueError as error:
            logger.warning("Bad request on %s: %s", self.path, error)
            self._send_json(400, {"error": str(error), "ok": False})
        except KeyError as error:
            logger.warning("Missing session on %s: %s", self.path, error)
            self._send_json(404, {"error": str(error), "ok": False})
        except Exception as error:  # pragma: no cover - defensive server path
            logger.exception("Unexpected server error on %s", self.path)
            self._send_json(500, {"error": str(error), "ok": False})


class CompanionServer:
    def __init__(self, config: CompanionConfig, storage: SessionStorage, processor: SessionProcessor) -> None:
        handler = type(
            "ConfiguredCompanionRequestHandler",
            (CompanionRequestHandler,),
            {
                "config": config,
                "storage": storage,
                "processor": processor,
            },
        )
        self._server = ReusableThreadingHTTPServer((config.host, config.port), handler)
        self._thread = Thread(target=self._server.serve_forever, name="meeting-companion-http", daemon=True)

    @property
    def bound_port(self) -> int:
        return int(self._server.server_address[1])

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> None:
        self._server.shutdown()
        self._server.server_close()
        self._thread.join(timeout=5)
