"""Isolated CI transport fixture. No external emails, persisted payloads or request logs."""
import json
import os
import threading
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

messages = {}
lock = threading.Lock()
reject_next = False
outage = False
outage_attempts = {}
conflict = False
conflict_attempts = {}
instance = os.environ.get("STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE", uuid.uuid4().hex)


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
            return self.respond(200, {"ready": True, "instance": instance})
        if self.path == "/outage":
            with lock:
                return self.respond(200, {"active": outage, "attempts": [
                    {"key": key, "attempts": count} for key, count in outage_attempts.items()]})
        if self.path == "/messages":
            with lock:
                return self.respond(200, list(messages.values()))
        if self.path == "/conflicts":
            with lock:
                return self.respond(200, {"active": conflict, "attempts": [
                    {"key": key, "attempts": entry["attempts"], "mismatches": entry["mismatches"]}
                    for key, entry in conflict_attempts.items()]})
        self.respond(404, {})

    def do_POST(self):
        global reject_next, outage, conflict
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
            if set(data) - {"reject_next", "outage", "conflict"} or any(type(value) is not bool for value in data.values()):
                return self.respond(400, {})
            with lock:
                reject_next = bool(data.get("reject_next"))
                if "outage" in data:
                    outage = data["outage"]
                if "conflict" in data:
                    conflict = data["conflict"]
                return self.respond(200, {"outage": outage})
        if self.path != "/emails":
            return self.respond(404, {})
        if self.headers.get("Authorization") != "Bearer ci-test-provider-key":
            return self.respond(401, {"message": "not authorized"})
        key = self.headers.get("Idempotency-Key")
        if not key or not all(recipient.endswith("@example.test") for recipient in data.get("to", [])):
            return self.respond(400, {})
        with lock:
            if key in conflict_attempts and conflict_attempts[key]["payload"] != data:
                conflict_attempts[key]["mismatches"] += 1
                return self.respond(409, {"name": "invalid_idempotent_request"})
            if conflict:
                entry = conflict_attempts.setdefault(key, {"payload": data, "attempts": 0, "mismatches": 0})
                entry["attempts"] += 1
                return self.respond(409, {"name": "concurrent_idempotent_requests"})
            if outage:
                outage_attempts[key] = outage_attempts.get(key, 0) + 1
                return self.respond(503, {"message": "controlled fixture outage"})
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


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", int(os.environ.get("STRATAAI_TEST_IDENTITY_PROVIDER_PORT", "19090"))), Handler).serve_forever()
