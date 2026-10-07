"""Isolated CI transport fixture. No external emails, persisted payloads or request logs."""
import json
import os
import threading
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

messages = {}
lock = threading.Lock()
reject_next = False


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def respond(self, status, data):
        encoded = json.dumps(data).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def do_GET(self):
        if self.path == "/health":
            return self.respond(200, {"ready": True})
        if self.path == "/messages":
            with lock:
                return self.respond(200, list(messages.values()))
        self.respond(404, {})

    def do_POST(self):
        global reject_next
        # JsonContent streams HTTP/1.1 requests using chunked transfer. Decode
        # that framing as well as fixed-length requests, with the same bound.
        try:
            if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
                body = bytearray()
                while True:
                    size = int(self.rfile.readline(128).split(b";", 1)[0].strip(), 16)
                    if size == 0:
                        for _ in range(32):
                            trailer = self.rfile.readline(8192)
                            if trailer == b"\r\n":
                                break
                            if not trailer:
                                return self.respond(400, {})
                        else:
                            return self.respond(400, {})
                        break
                    if size < 0 or len(body) + size > 65536:
                        return self.respond(413, {})
                    chunk = self.rfile.read(size)
                    if len(chunk) != size or self.rfile.read(2) != b"\r\n":
                        return self.respond(400, {})
                    body.extend(chunk)
            else:
                size = int(self.headers.get("Content-Length", "0"))
                if size < 0 or size > 65536:
                    return self.respond(413, {})
                body = self.rfile.read(size)
            data = json.loads(body)
            if not isinstance(data, dict):
                return self.respond(400, {})
        except (ValueError, EOFError):
            return self.respond(400, {})
        if self.path == "/control":
            with lock:
                reject_next = bool(data.get("reject_next"))
            return self.respond(200, {})
        if self.path != "/emails":
            return self.respond(404, {})
        if self.headers.get("Authorization") != "Bearer ci-test-provider-key":
            return self.respond(401, {"message": "not authorized"})
        key = self.headers.get("Idempotency-Key")
        if not key or not all(recipient.endswith("@example.test") for recipient in data.get("to", [])):
            return self.respond(400, {})
        with lock:
            if reject_next:
                reject_next = False
                return self.respond(401, {"message": "deliberately rejected fixture request"})
            if key in messages:
                entry = messages[key]
                if entry["payload"] != data:
                    return self.respond(409, {"message": "idempotency payload changed"})
                entry["attempts"] += 1
                return self.respond(200, {"id": entry["id"]})
            messages[key] = {"id": str(uuid.uuid4()), "key": key, "payload": data, "attempts": 1}
            # Provider accepted the effect but the caller saw an outage. Retry
            # must return the existing receipt without a second effect.
            return self.respond(503, {"message": "simulated lost acknowledgement"})


ThreadingHTTPServer(("0.0.0.0", int(os.environ.get("STRATAAI_TEST_IDENTITY_PROVIDER_PORT", "19090"))), Handler).serve_forever()
