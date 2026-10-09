"""ARCH-11-FR-040: bind all three built images to canonical provenance."""
import argparse
import importlib.util
import json
from pathlib import Path
import re

spec = importlib.util.spec_from_file_location("source_identity", Path(__file__).with_name("summarize-source-tests.py"))
identity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(identity)
HOSTS = ("web", "api", "worker")


def verify_labels(build, images):
    """Compare Docker inspection data with an already validated build identity."""
    if not isinstance(images, list) or len(images) != len(HOSTS):
        raise ValueError("incomplete_images")
    expected = {"org.opencontainers.image.source": "https://github.com/" + build["repository"],
                "org.opencontainers.image.revision": build["commitSha"],
                "org.opencontainers.image.version": build["version"],
                "org.opencontainers.image.created": build["createdAt"]}
    admitted = {}
    for image in images:
        if not isinstance(image, dict) or not isinstance(image.get("Id"), str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", image["Id"]):
            raise ValueError("invalid_image_identity")
        tags = image.get("RepoTags")
        if not isinstance(tags, list) or any(not isinstance(tag, str) for tag in tags):
            raise ValueError("invalid_image_tags")
        hosts = [host for host in HOSTS if build["images"][host] in tags]
        if len(hosts) != 1 or hosts[0] in admitted or image["Id"] in admitted.values():
            raise ValueError("different_images")
        config = image.get("Config")
        labels = config.get("Labels") if isinstance(config, dict) else None
        if not isinstance(labels, dict) or any(labels.get(key) != value for key, value in expected.items()):
            raise ValueError("different_image_provenance")
        admitted[hosts[0]] = image["Id"]
    if set(admitted) != set(HOSTS):
        raise ValueError("incomplete_images")
    return admitted


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--images", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--expected", type=Path)
    args = parser.parse_args()
    try:
        build = identity.build_identity(json.loads(args.metadata.read_text(encoding="utf-8")))
        ids = verify_labels(build, json.loads(args.images.read_text(encoding="utf-8")))
        record = {"schemaVersion": 1, "build": build, "imageIds": ids}
        if args.expected is not None:
            expected = json.loads(args.expected.read_text(encoding="utf-8"))
            verify_record(build, expected)
            if expected != record:
                raise ValueError("different_loaded_images")
        if args.output is not None:
            args.output.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print("Exactly three image identities and OCI source/revision/version/creation labels verified.")
        return 0
    except (ValueError, OSError, TypeError, KeyError, AttributeError):
        print("Image provenance verification failed.")
        return 1


def verify_record(build, value):
    """Admit only canonical public identity and three distinct Docker image IDs."""
    if not isinstance(value, dict) or set(value) != {"schemaVersion", "build", "imageIds"}:
        raise ValueError("invalid_image_record")
    if type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1 or value["build"] != build:
        raise ValueError("different_image_record")
    identity.build_identity(value["build"])
    ids = value["imageIds"]
    if not isinstance(ids, dict) or set(ids) != set(HOSTS) or any(
            not isinstance(item, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", item) for item in ids.values()):
        raise ValueError("invalid_image_record")
    if len(set(ids.values())) != len(HOSTS):
        raise ValueError("different_images")
    return ids


if __name__ == "__main__":
    raise SystemExit(main())
