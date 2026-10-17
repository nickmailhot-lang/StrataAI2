#!/usr/bin/env python3
"""Read-only integrity/freshness check for an operator-protected backup record."""
import argparse
from contextlib import contextmanager
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import sys
import time


def identity(value):
    fields = (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns)
    # Windows ctime is deprecated creation metadata, and its path/descriptor
    # views can differ. It is not the POSIX change-time concurrency guard.
    return fields if os.name == "nt" else fields + (value.st_ctime_ns,)


@contextmanager
def stable_file(path):
    before = os.stat(path, follow_symlinks=False)
    if not stat.S_ISREG(before.st_mode):
        raise ValueError("regular_file_required")
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(path, flags), "rb") as source:
        if identity(before) != identity(os.fstat(source.fileno())):
            raise ValueError("file_changed")
        yield source
        if identity(before) != identity(os.fstat(source.fileno())) or identity(before) != identity(os.stat(path, follow_symlinks=False)):
            raise ValueError("file_changed")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate_field")
        result[key] = value
    return result


def verify(record, archive, maximum_age, clock=time.time):
    if type(maximum_age) is not int or not 1 <= maximum_age <= 31536000:
        raise ValueError("age_limit")
    with stable_file(record) as source:
        content = source.read(4097)
        if len(content) > 4096:
            raise ValueError("record_size")
        try:
            value = json.loads(content, object_pairs_hook=unique_object)
        except RecursionError as error:
            raise ValueError("record_shape") from error
        if not isinstance(value, dict) or set(value) != {
                "schemaVersion", "startedAtUnixSeconds", "completedAtUnixSeconds", "archiveBytes", "sha256"}:
            raise ValueError("record_shape")
        if type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1:
            raise ValueError("record_version")
        for name in ["startedAtUnixSeconds", "completedAtUnixSeconds", "archiveBytes"]:
            if type(value[name]) is not int or value[name] <= 0:
                raise ValueError("record_number")
        start, end = value["startedAtUnixSeconds"], value["completedAtUnixSeconds"]
        if not start <= end <= 253402300799:
            raise ValueError("record_clock")
        if not isinstance(value["sha256"], str) or not re.fullmatch("[a-f0-9]{64}", value["sha256"]):
            raise ValueError("record_digest")
        with stable_file(archive) as encrypted:
            if os.fstat(encrypted.fileno()).st_size != value["archiveBytes"]:
                raise ValueError("archive_size")
            if hashlib.file_digest(encrypted, "sha256").hexdigest() != value["sha256"]:
                raise ValueError("archive_digest")
        # Evaluate freshness after reading the full archive. The start of the
        # dump bounds its data snapshot; a slow completion cannot renew that age.
        now = clock()
        if not isinstance(now, (int, float)) or not math.isfinite(now) or now < end:
            raise ValueError("record_future")
        age = now - start
        if age > maximum_age:
            raise ValueError("record_stale")
    return {"schemaVersion": 1, "status": "verified", "ageSeconds": math.ceil(age),
            "maximumAgeSeconds": maximum_age, "archiveBytes": value["archiveBytes"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("record", type=Path)
    parser.add_argument("archive", type=Path)
    parser.add_argument("maximum_age_seconds")
    args = parser.parse_args()
    try:
        if not re.fullmatch("[1-9][0-9]{0,7}", args.maximum_age_seconds):
            raise ValueError("age_limit")
        result = verify(args.record, args.archive, int(args.maximum_age_seconds))
    except (ValueError, OSError, TypeError):
        print("Backup record, archive integrity or freshness verification failed.", file=sys.stderr)
        return 1
    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
