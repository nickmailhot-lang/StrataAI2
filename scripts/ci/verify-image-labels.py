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
    args = parser.parse_args()
    try:
        build = identity.build_identity(json.loads(args.metadata.read_text(encoding="utf-8")))
        verify_labels(build, json.loads(args.images.read_text(encoding="utf-8")))
        print("Exactly three image identities and OCI source/revision/version/creation labels verified.")
        return 0
    except (ValueError, OSError, TypeError, KeyError, AttributeError):
        print("Image provenance verification failed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
