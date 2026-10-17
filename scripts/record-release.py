#!/usr/bin/env python3
"""Record a manually deployed release after bounded, read-only runtime checks."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tarfile
import tempfile

HOSTS = ("web", "api", "worker")
BUILD_FIELDS = {"repository", "commitSha", "shortSha", "workflowRunId", "workflowRunNumber",
                "workflowRunAttempt", "version", "releaseVersion", "imageTag", "createdAt", "images"}


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate_field")
        result[key] = value
    return result


def read_private(path, limit):
    before = path.lstat()
    if not stat.S_ISREG(before.st_mode):
        raise ValueError("regular_file_required")
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(path, flags), "rb") as source:
        opened = os.fstat(source.fileno())
        content = source.read(limit + 1)
        after = path.lstat()
        if len(content) > limit or any((item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns) !=
                (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) for item in (opened, after, os.fstat(source.fileno()))):
            raise ValueError("file_changed_or_oversized")
    return content


def parse(content):
    try:
        return json.loads(content, object_pairs_hook=unique_object)
    except RecursionError as error:
        raise ValueError("record_shape") from error


def build(value):
    if not isinstance(value, dict) or set(value) != BUILD_FIELDS:
        raise ValueError("build_shape")
    patterns = {"repository": r"[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}",
                "commitSha": r"(?:[a-f0-9]{40}|[a-f0-9]{64})", "version": r"[0-9A-Za-z][0-9A-Za-z.+-]{0,79}",
                "workflowRunId": r"[1-9][0-9]{0,31}", "workflowRunNumber": r"[1-9][0-9]{0,31}",
                "workflowRunAttempt": r"[1-9][0-9]{0,31}", "createdAt": r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3}Z"}
    if any(not isinstance(value[key], str) or not re.fullmatch(pattern, value[key]) for key, pattern in patterns.items()):
        raise ValueError("build_identity")
    datetime.fromisoformat(value["createdAt"])
    if value["shortSha"] != value["commitSha"][:12] or value["imageTag"] != value["commitSha"] or value["releaseVersion"] not in (None, value["version"]):
        raise ValueError("build_identity")
    if value["images"] != {host: f"strataai-{host}:{value['commitSha']}" for host in HOSTS}:
        raise ValueError("image_references")


def image_ids(value):
    if not isinstance(value, dict) or set(value) != set(HOSTS) or any(not isinstance(v, str) or
            not re.fullmatch(r"sha256:[a-f0-9]{64}", v) for v in value.values()):
        raise ValueError("image_ids")


def snapshot(value):
    if not isinstance(value, dict) or set(value) != {"build", "imageIds", "buildMetadataSha256", "imageProvenanceSha256", "acceptedAt"}:
        raise ValueError("snapshot_shape")
    build(value["build"])
    image_ids(value["imageIds"])
    for field in ("buildMetadataSha256", "imageProvenanceSha256"):
        if not isinstance(value[field], str) or not re.fullmatch(r"[a-f0-9]{64}", value[field]):
            raise ValueError("snapshot_digest")
    if not isinstance(value["acceptedAt"], str) or not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", value["acceptedAt"]):
        raise ValueError("snapshot_clock")
    datetime.fromisoformat(value["acceptedAt"])


@contextmanager
def deployment_lock(path):
    # Keep the one-byte lock file: deleting it could split concurrent locks
    # across two inodes. OS locks release automatically when a process exits.
    flags = os.O_CREAT | os.O_RDWR | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    if path.is_symlink():
        raise ValueError("linked_lock")
    with os.fdopen(os.open(path, flags, 0o600), "r+b") as locked:
        if not stat.S_ISREG(os.fstat(locked.fileno()).st_mode):
            raise ValueError("lock_type")
        if os.fstat(locked.fileno()).st_size == 0:
            locked.write(b"0"); locked.flush()
        locked.seek(0)
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(locked.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(locked.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        selected = path.lstat(); opened = os.fstat(locked.fileno())
        if not stat.S_ISREG(selected.st_mode) or (selected.st_dev, selected.st_ino) != (opened.st_dev, opened.st_ino):
            raise ValueError("lock_changed")
        yield


def runtime_check(expected):
    timeout = os.environ.get("STRATAAI_HEALTH_TIMEOUT_SECONDS", "30")
    if not re.fullmatch(r"[1-9][0-9]{0,2}", timeout) or not 1 <= int(timeout) <= 300:
        raise ValueError("health_timeout")
    budget = int(timeout)
    result = subprocess.run(["bash", str(Path(__file__).with_name("verify-release-runtime.sh")), str(expected)],
                            capture_output=True, timeout=6 * budget + 4 * min(budget, 15) + 10)
    if result.returncode:
        raise ValueError("runtime_not_verified")


def archive_bound_runtime_image(query, bundle, metadata, host, actual, expected):
    """Admit an ID representation change only through the original OCI bytes."""
    template = '{"Id":{{json .Id}},"Config":{"Labels":{{json .Config.Labels}}},"Descriptor":{{json (index . "Descriptor")}}}'
    try:
        image = parse(query(["image", "inspect", "--format", template, actual]))
        if not isinstance(image, dict) or image.get("Id") != actual:
            raise ValueError("loaded_image_identity")
        labels = image["Config"]["Labels"]
        required = {"org.opencontainers.image.source": "https://github.com/" + metadata["repository"],
                    "org.opencontainers.image.revision": metadata["commitSha"],
                    "org.opencontainers.image.version": metadata["version"],
                    "org.opencontainers.image.created": metadata["createdAt"]}
        if not isinstance(labels, dict) or any(labels.get(key) != value for key, value in required.items()):
            raise ValueError("loaded_image_labels")
        companion = Path(__file__).with_name("verify-image-labels.py")
        if not companion.is_file():
            # Source checkout layout; assembled bundles carry the same helper beside this command.
            companion = Path(__file__).parent / "ci/verify-image-labels.py"
        spec = importlib.util.spec_from_file_location("release_archive_binding", companion)
        verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
        verifier.archive_image_binding(bundle / "images" / f"strataai-{host}.tar.gz", image, expected)
    except (ValueError, OSError, KeyError, TypeError, AttributeError, tarfile.TarError):
        raise ValueError("loaded_image_not_bound_to_original_archive") from None


def running_containers(project, expected_ids, bundle=None, metadata=None, bindings=None):
    # Read only identity/state/Compose labels. Never capture container config,
    # environment, mounts, logs, credentials or provider response bodies.
    def query(args):
        result = subprocess.run(["docker", *args], capture_output=True, text=True,
                                encoding="utf-8", timeout=10)
        if result.returncode or len(result.stdout) > 65536:
            raise ValueError("container_inspection_failed")
        return result.stdout

    result = {}
    fields = [".Id", ".Image", ".State.Running", ".State.Paused", ".State.Restarting", ".State.StartedAt",
              '(index .Config.Labels "com.docker.compose.project")',
              '(index .Config.Labels "com.docker.compose.service")',
              '(index .Config.Labels "com.docker.compose.oneoff")']
    template = "[" + ",".join("{{json " + field + "}}" for field in fields) + "]"
    for host in HOSTS:
        ids = query(["ps", "--all", "--no-trunc", "--filter", "label=com.docker.compose.project=" + project,
                     "--filter", "label=com.docker.compose.service=" + host,
                     "--filter", "label=com.docker.compose.oneoff=False", "--format", "{{.ID}}"]).splitlines()
        if not ids or len(set(ids)) != len(ids) or any(not re.fullmatch(r"[a-f0-9]{64}", id) for id in ids):
            raise ValueError("container_cardinality")
        result[host] = []
        for id in sorted(ids):
            row = parse(query(["inspect", "--format", template, id]))
            if not isinstance(row, list) or len(row) != 9 or row[0] != id \
                    or not isinstance(row[1], str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", row[1]) \
                    or row[2] is not True or row[3] is not False or row[4] is not False \
                    or row[6:] != [project, host, "False"]:
                raise ValueError("container_identity_or_state")
            if row[1] != expected_ids[host]:
                if bundle is None or metadata is None or bindings is None:
                    raise ValueError("container_image_identity")
                key = (host, row[1])
                if key not in bindings:
                    archive_bound_runtime_image(query, bundle, metadata, host, row[1], expected_ids[host])
                    bindings.add(key)
            if not isinstance(row[5], str) or not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,9})?Z", row[5]):
                raise ValueError("container_start_identity")
            datetime.fromisoformat(row[5])
            result[host].append(row[:2] + [row[5]])
    return result


def accept(bundle, state, environment, project, initialize=False, verify=runtime_check):
    if not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,63}", environment) or not re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,62}", project):
        raise ValueError("deployment_identity")
    state = state.parent.resolve(strict=True) / state.name
    if state.is_relative_to(bundle.resolve(strict=True)):
        raise ValueError("state_must_be_outside_bundle")
    metadata_path = bundle / "build-metadata.json"; provenance_path = bundle / "images/image-provenance.json"
    raw = read_private(metadata_path, 4096); metadata = parse(raw); build(metadata)
    provenance_raw = read_private(provenance_path, 16384); provenance = parse(provenance_raw)
    if not isinstance(provenance, dict) or set(provenance) != {"schemaVersion", "build", "imageIds"} or type(provenance["schemaVersion"]) is not int or provenance["schemaVersion"] != 1 or provenance["build"] != metadata:
        raise ValueError("provenance_identity")
    image_ids(provenance["imageIds"])
    candidate = {"build": metadata, "imageIds": provenance["imageIds"], "buildMetadataSha256": hashlib.sha256(raw).hexdigest(),
                 "imageProvenanceSha256": hashlib.sha256(provenance_raw).hexdigest()}
    with deployment_lock(state.with_name(state.name + ".lock")):
        previous_bytes = read_private(state, 32768) if state.exists() or state.is_symlink() else None
        previous = parse(previous_bytes) if previous_bytes is not None else None
        if previous_bytes is None:
            if not initialize:
                raise ValueError("initialization_required")
        else:
            if initialize or not isinstance(previous, dict) or set(previous) != {"schemaVersion", "environment", "composeProject", "current", "previous"} or type(previous["schemaVersion"]) is not int or previous["schemaVersion"] != 1 or previous["environment"] != environment or previous["composeProject"] != project:
                raise ValueError("state_identity")
            snapshot(previous["current"])
            if previous["previous"] is not None:
                snapshot(previous["previous"])
        bindings = set()
        containers = running_containers(project, provenance["imageIds"], bundle, metadata, bindings)
        # The verifier receives captured expected bytes, never a mutable
        # candidate path. Re-read both original inputs before publishing.
        with tempfile.TemporaryDirectory(prefix="strataai-release-acceptance-") as scratch:
            expected = Path(scratch) / "expected.json"; expected.write_bytes(raw)
            verify(expected)
        if running_containers(project, provenance["imageIds"], bundle, metadata, bindings) != containers:
            raise ValueError("running_containers_changed")
        if read_private(metadata_path, 4096) != raw or read_private(provenance_path, 16384) != provenance_raw:
            raise ValueError("candidate_changed")
        current_bytes = read_private(state, 32768) if state.exists() or state.is_symlink() else None
        if current_bytes != previous_bytes:
            raise ValueError("state_changed")
        if previous is not None and {k: v for k, v in previous["current"].items() if k != "acceptedAt"} == candidate:
            return "already-recorded"
        candidate["acceptedAt"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        value = {"schemaVersion": 1, "environment": environment, "composeProject": project,
                 "current": candidate, "previous": previous["current"] if previous else None}
        descriptor, temporary = tempfile.mkstemp(prefix=".strataai-release-", dir=state.parent)
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as output:
                json.dump(value, output, indent=2); output.write("\n"); output.flush(); os.fsync(output.fileno())
            os.replace(temporary, state)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
    return "recorded"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--state", required=True, type=Path)
    parser.add_argument("--environment", required=True)
    parser.add_argument("--project", required=True)
    parser.add_argument("--initialize", action="store_true")
    args = parser.parse_args()
    try:
        status = accept(args.bundle, args.state, args.environment, args.project, args.initialize)
    except (ValueError, OSError, TypeError, subprocess.SubprocessError):
        print("Release acceptance was not confirmed; retain and inspect the existing release record.", file=sys.stderr)
        return 1
    print("Release runtime verified; deployment record " + status + ".")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
