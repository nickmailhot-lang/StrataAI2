"""ARCH-11-FR-040: bind all three built images to canonical provenance."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tarfile

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


def archive_image_binding(path, image, expected_id, *, loaded_descriptor=True):
    """Prove a config/manifest ID conversion using the original saved OCI bytes.

    Labels alone cannot authorize a different loaded ID. Read small metadata
    from the archive without extracting layers or copying a runtime tree.
    """
    if path.is_symlink() or not path.is_file():
        raise ValueError("missing_image_archive")
    records = {}
    seen = set()
    with tarfile.open(path, "r|gz") as archive:
        for member in archive:
            if member.name != "index.json" and not re.fullmatch(r"blobs/sha256/[a-f0-9]{64}", member.name):
                continue
            if not member.isfile():
                raise ValueError("invalid_archive_metadata")
            if member.name in seen or len(seen) >= 1024:
                raise ValueError("duplicate_archive_metadata")
            seen.add(member.name)
            if member.size > 1024 * 1024:
                continue  # Layer bytes are loaded/verified by Docker, never extracted here.
            if len(records) >= 128:
                raise ValueError("duplicate_archive_metadata")
            raw = archive.extractfile(member).read()
            if member.name.startswith("blobs/") and hashlib.sha256(raw).hexdigest() != member.name.rsplit("/", 1)[1]:
                raise ValueError("different_archive_blob")
            records[member.name] = raw
    index = archive_object(records["index.json"])
    if index.get("schemaVersion") != 2 or not isinstance(index.get("manifests"), list) or len(index["manifests"]) != 1:
        raise ValueError("invalid_archive_index")
    descriptor = index["manifests"][0]
    if not isinstance(descriptor, dict):
        raise ValueError("invalid_archive_manifest")
    digest = descriptor["digest"]
    if not isinstance(digest, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", digest):
        raise ValueError("invalid_archive_manifest")
    raw_manifest = records["blobs/sha256/" + digest[7:]]
    manifest = archive_object(raw_manifest)
    manifest_types = {"application/vnd.oci.image.manifest.v1+json", "application/vnd.docker.distribution.manifest.v2+json"}
    if descriptor.get("mediaType") not in manifest_types or type(descriptor.get("size")) is not int or descriptor["size"] != len(raw_manifest) \
            or manifest.get("schemaVersion") != 2 or manifest.get("mediaType") != descriptor["mediaType"]:
        raise ValueError("invalid_archive_manifest")
    config = manifest["config"]
    if not isinstance(config, dict):
        raise ValueError("invalid_archive_config")
    config_id = config["digest"]
    if not isinstance(config_id, str) or not re.fullmatch(r"sha256:[a-f0-9]{64}", config_id):
        raise ValueError("invalid_archive_config")
    raw_config = records["blobs/sha256/" + config_id[7:]]
    if type(config.get("size")) is not int or config["size"] != len(raw_config) or config.get("mediaType") not in {
            "application/vnd.oci.image.config.v1+json", "application/vnd.docker.container.image.v1+json"}:
        raise ValueError("invalid_archive_config")
    archive_config = archive_object(raw_config).get("config")
    if not isinstance(archive_config, dict) or archive_config.get("Labels") != image["Config"]["Labels"]:
        raise ValueError("different_archive_labels")
    if expected_id not in (config_id, digest) or image["Id"] not in (config_id, digest):
        raise ValueError("different_loaded_images")
    # Docker inspections must also prove their loaded manifest descriptor.
    # SBOMs have no Docker Descriptor; their IDs/labels still require the same
    # cryptographic config/manifest binding above.
    if loaded_descriptor and image["Id"] == digest:
        loaded = image.get("Descriptor")
        if not isinstance(loaded, dict) or loaded.get("digest") != digest \
                or loaded.get("mediaType") != descriptor["mediaType"] or loaded.get("size") != len(raw_manifest):
            raise ValueError("different_loaded_manifest")


def archive_object(raw):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ValueError("duplicate_archive_json")
            value[key] = item
        return value
    value = json.loads(raw, object_pairs_hook=unique)
    if not isinstance(value, dict):
        raise ValueError("invalid_archive_json")
    return value


def verify_loaded_record(build, images, expected, archives=None):
    loaded = verify_labels(build, images)
    retained = verify_record(build, expected)
    for image in images:
        host = next(host for host in HOSTS if build["images"][host] in image["RepoTags"])
        if loaded[host] != retained[host]:
            if archives is None:
                raise ValueError("different_loaded_images")
            archive_image_binding(archives / f"strataai-{host}.tar.gz", image, retained[host])
    return expected


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--images", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--expected", type=Path)
    parser.add_argument("--archives", type=Path)
    args = parser.parse_args()
    try:
        build = identity.build_identity(json.loads(args.metadata.read_text(encoding="utf-8")))
        images = json.loads(args.images.read_text(encoding="utf-8"))
        ids = verify_labels(build, images)
        record = {"schemaVersion": 1, "build": build, "imageIds": ids}
        if args.archives is not None and args.expected is None:
            raise ValueError("missing_expected_record")
        if args.expected is not None:
            expected = json.loads(args.expected.read_text(encoding="utf-8"))
            record = verify_loaded_record(build, images, expected, args.archives)
        if args.output is not None:
            args.output.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print("Exactly three image identities and OCI source/revision/version/creation labels verified.")
        return 0
    except (ValueError, OSError, TypeError, KeyError, AttributeError, tarfile.TarError):
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
