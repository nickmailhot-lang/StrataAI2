"""Disposable clamd protocol simulator, not a malware engine."""
import os
import socket
import struct
import sys
import threading

if os.environ.get("CI") != "true":
    raise SystemExit("Scanner protocol fixtures require CI.")
path, counter = sys.argv[1:]
lock = threading.Lock()
scans = 0
# Harmless protocol-fixture marker, deliberately not a malware signature.
reject_marker = b"STRATAAI_CI_HARMLESS_REJECT_FIXTURE"

def exact(connection, size):
    data = bytearray()
    while len(data) < size:
        part = connection.recv(size - len(data))
        if not part:
            raise ValueError("Incomplete protocol record")
        data.extend(part)
    return bytes(data)

def serve(connection):
    global scans
    with connection:
        connection.settimeout(30)
        try:
            command = bytearray()
            while len(command) < 16:
                byte = exact(connection, 1)
                if byte == b"\0":
                    break
                command.extend(byte)
            if command == b"zPING":
                connection.sendall(b"PONG\0")
                return
            if command != b"zINSTREAM":
                return
            size = 0
            payload = bytearray()
            while True:
                length = struct.unpack(">I", exact(connection, 4))[0]
                if not length:
                    break
                size += length
                if size > 1024 * 1024:
                    return
                payload.extend(exact(connection, length))
            if not size:
                return
            with lock:
                scans += 1
                with open(counter, "w", encoding="ascii") as output:
                    output.write(str(scans))
            connection.sendall(b"stream: StrataAI.CI.HarmlessFixture FOUND\0"
                               if reject_marker in payload else b"stream: OK\0")
        except (OSError, ValueError):
            return

server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
server.bind(path)
os.chmod(path, 0o600)
server.listen(16)
while True:
    connection, _ = server.accept()
    threading.Thread(target=serve, args=(connection,), daemon=True).start()
