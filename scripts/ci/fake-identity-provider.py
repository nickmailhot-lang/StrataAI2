"""Isolated CI transport fixture. No external emails, persisted payloads or request logs."""
import json
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
        size = int(self.headers.get("Content-Length", "0"))
        if size > 65536:
            return self.respond(413, {})
        data = json.loads(self.rfile.read(size))
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


ThreadingHTTPServer(("0.0.0.0", 19090), Handler).serve_forever()
