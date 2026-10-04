"""Exercise the declared scanner fixture, never claim malware-engine accuracy."""
import os
import socket
import struct
import subprocess
import sys
import tempfile
import time

assert os.environ.get("CI") == "true", "Scanner fixture checks require CI."
assert hasattr(socket, "AF_UNIX"), "Scanner fixture checks require Unix sockets."
def reply(connection):
    record = bytearray()
    while len(record) < 64:
        part = connection.recv(1)
        assert part, "Scanner fixture reply ended early."
        record.extend(part)
        if part == b"\0":
            return bytes(record)
    raise AssertionError("Scanner fixture reply exceeded its bound.")

with tempfile.TemporaryDirectory(prefix="strata-scanner-") as root:
    path, counter = os.path.join(root, "s.sock"), os.path.join(root, "count")
    child = subprocess.Popen([sys.executable, "scripts/ci/fake-attachment-scanner.py", path, counter],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            assert child.poll() is None, "Scanner fixture stopped before readiness."
            if os.path.exists(path):
                try:
                    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as probe:
                        probe.settimeout(1)
                        probe.connect(path)
                        probe.sendall(b"zPING\0")
                        if reply(probe) == b"PONG\0":
                            break
                except OSError:
                    pass
            time.sleep(0.02)
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
            connection.settimeout(5)
            connection.connect(path)
            connection.sendall(b"zPING\0")
            assert reply(connection) == b"PONG\0"
        for fragments in [[b"PNG"], [b"P", b"N", b"G"]]:
            with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
                connection.settimeout(5)
                connection.connect(path)
                connection.sendall(b"zINSTREAM\0")
                for fragment in fragments:
                    connection.sendall(struct.pack(">I", len(fragment)) + fragment)
                connection.sendall(bytes(4))
                assert reply(connection) == b"stream: OK\0"
        with open(counter, encoding="ascii") as result:
            assert result.read() == "2"
        print("Declared scanner fixture PING, framed/fragmented streams and scan counts passed.")
    finally:
        child.terminate()
        child.wait(timeout=5)
