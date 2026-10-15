"""Bounded validator/repacker for the CI fixture's Data Protection key archive.

Never extracts input paths. Only canonical flat key XML files enter the target
container; original tar paths, links, ownership and permissions are discarded.
This is a recovery rehearsal helper, not a general production backup utility.
"""
import hashlib
import io
import os
from pathlib import Path
import re
import sys
import tarfile
import xml.etree.ElementTree as ET

LIMIT = 2 * 1024 * 1024
KEY_NAME = re.compile(r"(?:\./)?key-([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.xml\Z")


def validate(path, empty=False):
    with Path(path).open("rb") as stream:
        data = stream.read(LIMIT + 1)
    if len(data) > LIMIT:
        raise ValueError()
    files = {}
    root_seen = False
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:") as archive:
        for entry in archive:
            if entry.name in (".", "./"):
                if root_seen or not entry.isdir() or entry.size != 0:
                    raise ValueError()
                root_seen = True
                continue
            match = KEY_NAME.fullmatch(entry.name)
            if not match or not entry.isfile() or entry.issparse() or not 1 <= entry.size <= 65536:
                raise ValueError()
            name = "key-" + match[1] + ".xml"
            if name in files or len(files) == 16:
                raise ValueError()
            content = archive.extractfile(entry).read(65537)
            document = content.decode("utf-8-sig")
            if len(content) != entry.size or "<!DOCTYPE" in document.upper() or "<!ENTITY" in document.upper():
                raise ValueError()
            declaration = re.match(r"\s*<\?xml\b(.*?)\?>", document, flags=re.DOTALL)
            encoding = re.search(r'''\bencoding\s*=\s*["']([^"']+)["']''', declaration[1], re.I) if declaration else None
            if encoding and encoding[1].lower() not in ("utf-8", "utf8"):
                raise ValueError()
            element = ET.fromstring(content)
            if element.tag != "key" or element.get("id") != match[1] or element.get("version") != "1":
                raise ValueError()
            files[name] = content
    if bool(files) == empty:
        raise ValueError()
    return files


def repack(files, output):
    # Exclusive output; validate everything before creating any plaintext file.
    with os.fdopen(os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "wb") as stream:
        with tarfile.open(fileobj=stream, mode="w", format=tarfile.USTAR_FORMAT) as archive:
            for name, content in sorted(files.items()):
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                entry.mode = 0o600
                entry.uid = entry.gid = 0
                archive.addfile(entry, io.BytesIO(content))


def fingerprint(files):
    result = hashlib.sha256(b"StrataAI CI recovered keys v1\0")
    for name, content in sorted(files.items()):
        result.update(name.encode() + b"\0" + len(content).to_bytes(8, "big") + content)
    return result.hexdigest()


if __name__ == "__main__":
    try:
        if len(sys.argv) != 3:
            raise ValueError()
        empty = sys.argv[1] == "--empty"
        files = validate(sys.argv[2] if empty else sys.argv[1], empty)
        if not empty:
            repack(files, sys.argv[2])
        sys.stdout.buffer.write(fingerprint(files).encode() + b"\n")
    except (ValueError, OSError, LookupError, UnicodeError, tarfile.TarError, ET.ParseError):
        sys.stderr.buffer.write(b"Recovery key archive refused.\n")
        sys.exit(1)
