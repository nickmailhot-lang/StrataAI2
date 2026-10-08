"""Fail closed on incomplete, changed, or mismatched release evidence."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re

spec = importlib.util.spec_from_file_location("source_identity", Path(__file__).with_name("summarize-source-tests.py"))
identity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(identity)
HOSTS = ("web", "api", "worker")


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def verify_checksums(root):
    files = {}
    for path in root.rglob("*"):
        if path.is_symlink():
            raise ValueError("linked_payload")
        if path.is_dir():
            continue
        if not path.is_file():
            raise ValueError("invalid_payload")
        name = path.relative_to(root).as_posix()
        if name != "SHA256SUMS":
            files[name] = path
    expected = {}
    for line in (root / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
        match = re.fullmatch(r"([a-f0-9]{64}) [ *](.+)", line)
        if not match:
            raise ValueError("invalid_checksum")
        name = PurePosixPath(match[2])
        if name.is_absolute() or ".." in name.parts or "\\" in match[2] or ":" in match[2] or name.as_posix() in expected:
            raise ValueError("invalid_checksum_path")
        expected[name.as_posix()] = match[1]
    if not files or set(files) != set(expected):
        raise ValueError("incomplete_checksums")
    for name, path in files.items():
        if digest(path) != expected[name]:
            raise ValueError("checksum_mismatch")
    return files


def metadata(root):
    value = identity.build_identity(json.loads((root / "build-metadata.json").read_text(encoding="utf-8")))
    for variable, field in (("GITHUB_SHA", "commitSha"), ("GITHUB_REPOSITORY", "repository"),
                            ("GITHUB_RUN_ID", "workflowRunId"), ("STRATAAI_BUILD_VERSION", "version")):
        if os.environ.get(variable) and os.environ[variable] != value[field]:
            raise ValueError("wrong_identity")
    return value


def sboms(root, build):
    for host in HOSTS:
        value = json.loads((root / f"strataai-{host}.cdx.json").read_text(encoding="utf-8"))
        component = value.get("metadata", {}).get("component", {})
        if value.get("bomFormat") != "CycloneDX" or component.get("type") != "container" or component.get("name") != build["images"][host] or not value.get("components"):
            raise ValueError("missing_or_wrong_sbom")
    receiver = json.loads((root / "metrics-collector.cdx.json").read_text(encoding="utf-8"))
    if receiver.get("bomFormat") != "CycloneDX" or receiver.get("metadata", {}).get("component", {}).get("type") != "container" or not receiver.get("components"):
        raise ValueError("missing_receiver_sbom")


def verify_inputs(images, security):
    verify_checksums(images)
    verify_checksums(security)
    build = metadata(images)
    metadata(security)
    if (images / "build-metadata.json").read_bytes() != (security / "build-metadata.json").read_bytes():
        raise ValueError("different_security_identity")
    for host in HOSTS:
        if not (images / f"strataai-{host}.tar.gz").stat().st_size:
            raise ValueError("empty_image")
    sboms(security, build)
    return build


def verify_bundle(bundle):
    files = verify_checksums(bundle)
    build = metadata(bundle)
    required = {"compose.release.yml", "compose.metrics.yml", "compose.attachments.yml", ".env.release.example", "README.md",
                "health-check.sh", "apply-migrations.sh", "migration-stream.sh", "provision-runtime-roles.sh", "deploy/metrics/collector.yml", "db/provision-runtime-roles.sql"}
    required.update(f"images/strataai-{host}.tar.gz" for host in HOSTS)
    if not required <= set(files) or not any(name.startswith("db/migrations/") and name.endswith(".sql") for name in files):
        raise ValueError("incomplete_bundle")
    if any(any(part.startswith(".") for part in PurePosixPath(name).parts) and name != ".env.release.example" for name in files):
        raise ValueError("unexpected_hidden_file")
    for host in HOSTS:
        if not files[f"images/strataai-{host}.tar.gz"].stat().st_size:
            raise ValueError("empty_image")
    allowed_root = required | {"build-metadata.json"}
    if any("/" not in name and name not in allowed_root for name in files):
        raise ValueError("unexpected_root_file")
    if any("/" in name and name.split("/", 1)[0] not in {"images", "sbom", "security", "db", "deploy"} for name in files):
        raise ValueError("unexpected_directory")
    security = bundle / "security"
    verify_checksums(security)
    metadata(security)
    if (security / "build-metadata.json").read_bytes() != (bundle / "build-metadata.json").read_bytes():
        raise ValueError("different_security_identity")
    sboms(security, build)
    sboms(bundle / "sbom", build)
    for name in [*(f"strataai-{host}.cdx.json" for host in HOSTS), "metrics-collector.cdx.json"]:
        if (security / name).read_bytes() != (bundle / "sbom" / name).read_bytes():
            raise ValueError("changed_sbom_copy")
    return build


def main():
    parser = argparse.ArgumentParser()
    mode = parser.add_subparsers(dest="mode", required=True)
    inputs = mode.add_parser("inputs")
    inputs.add_argument("--images", required=True, type=Path)
    inputs.add_argument("--security", required=True, type=Path)
    bundle = mode.add_parser("bundle")
    bundle.add_argument("--path", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.mode == "inputs": verify_inputs(args.images, args.security)
        else: verify_bundle(args.path)
        print("Release evidence identity, completeness and checksums verified.")
        return 0
    except (ValueError, OSError, TypeError, KeyError, AttributeError):
        print("Release evidence verification failed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
