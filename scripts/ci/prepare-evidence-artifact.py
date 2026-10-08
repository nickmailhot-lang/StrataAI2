"""Stage only explicitly selected evidence with canonical identity and checksums."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import tempfile

spec = importlib.util.spec_from_file_location("source_identity", Path(__file__).with_name("summarize-source-tests.py"))
identity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(identity)
RESERVED = {"build-metadata.json", "SHA256SUMS"}


def stage(metadata_path, output, entries):
    raw = metadata_path.read_bytes()
    metadata = identity.build_identity(json.loads(raw))
    for variable, field in (("GITHUB_SHA", "commitSha"), ("GITHUB_REPOSITORY", "repository"),
                            ("GITHUB_RUN_ID", "workflowRunId"), ("STRATAAI_BUILD_VERSION", "version")):
        if os.environ.get(variable) and os.environ[variable] != metadata[field]:
            raise ValueError("wrong_identity")
    selected = {}
    for source, destination in entries:
        source = Path(source)
        dest = PurePosixPath(destination)
        if dest.is_absolute() or ".." in dest.parts or "\\" in destination or ":" in destination:
            raise ValueError("invalid_destination")
        if any(path.is_symlink() for path in (source, *source.parents)):
            raise ValueError("linked_input")
        if not source.exists():
            continue
        paths = [source] if source.is_file() else list(source.rglob("*"))
        for path in paths:
            if path.is_symlink():
                raise ValueError("linked_input")
            if path.is_dir():
                continue
            if not path.is_file():
                raise ValueError("invalid_input")
            target = dest if source.is_file() else dest / path.relative_to(source).as_posix()
            name = target.as_posix()
            # Match upload-artifact's default hidden-file exclusion so every
            # checksum names a payload the uploader will actually retain.
            if any(part.startswith(".") for part in target.parts):
                continue
            if name == "." or name in RESERVED or any(c in name for c in "\r\n\\") or name in selected:
                raise ValueError("invalid_or_duplicate_destination")
            selected[name] = path
    if not selected:
        print("No selected evidence exists; artifact remains absent.")
        return False
    if output.exists():
        raise ValueError("output_already_exists")
    output.parent.mkdir(parents=True, exist_ok=True)
    # Publish only after every payload and checksum is written. On failure the
    # private temporary directory is never selected by the artifact uploader.
    temporary = Path(tempfile.mkdtemp(prefix=".evidence-", dir=output.parent))
    (temporary / "build-metadata.json").write_bytes(raw)
    for name, source in selected.items():
        target = temporary / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    sums = []
    for name in sorted([*selected, "build-metadata.json"]):
        digest = hashlib.sha256()
        with (temporary / name).open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        sums.append(f"{digest.hexdigest()}  {name}\n")
    (temporary / "SHA256SUMS").write_text("".join(sums), encoding="utf-8", newline="\n")
    temporary.rename(output)
    print("Selected evidence staged with canonical identity and checksums.")
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--entry", nargs=2, action="append", required=True)
    args = parser.parse_args()
    try:
        stage(args.metadata, args.output, args.entry)
        return 0
    except (ValueError, OSError, TypeError, KeyError):
        print("Evidence identity or staging failed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
