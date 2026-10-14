import copy
import hashlib
import io
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import tarfile
import unittest

spec = importlib.util.spec_from_file_location("image_labels", Path(__file__).parents[1] / "scripts/ci/verify-image-labels.py")
labels = importlib.util.module_from_spec(spec)
spec.loader.exec_module(labels)
SHA = "a" * 40
META = {"repository": "example/StrataAI2", "commitSha": SHA, "shortSha": SHA[:12], "workflowRunId": "123", "workflowRunNumber": "1",
        "workflowRunAttempt": "1", "version": "0.1.0-1", "releaseVersion": None, "imageTag": SHA, "createdAt": "2026-10-09T00:00:00.000Z",
        "images": {host: f"strataai-{host}:{SHA}" for host in labels.HOSTS}}


def inspection():
    return [{"Id": "sha256:" + str(index + 1) * 64, "RepoTags": [META["images"][host]], "Config": {"Labels": {
        "org.opencontainers.image.source": "https://github.com/" + META["repository"],
        "org.opencontainers.image.revision": SHA, "org.opencontainers.image.version": META["version"],
        "org.opencontainers.image.created": META["createdAt"]}}} for index, host in enumerate(labels.HOSTS)]


class ImageProvenanceTests(unittest.TestCase):
    def test_admits_three_exact_provenance_identities_independent_of_inspection_order(self):
        images = inspection()
        expected = {host: image["Id"] for host, image in zip(labels.HOSTS, images)}
        self.assertEqual(labels.verify_labels(META, list(reversed(images))), expected)

    def test_missing_or_different_labels_on_each_host_refuse_the_candidate(self):
        for index, host in enumerate(labels.HOSTS):
            for key in inspection()[index]["Config"]["Labels"]:
                for missing in (True, False):
                    with self.subTest(host=host, key=key, missing=missing):
                        images = inspection()
                        if missing: del images[index]["Config"]["Labels"][key]
                        else: images[index]["Config"]["Labels"][key] = "PRIVATE WRONG VALUE"
                        with self.assertRaises(ValueError): labels.verify_labels(META, images)

    def test_missing_duplicate_extra_and_shared_image_identities_refused(self):
        images = inspection()
        shared = copy.deepcopy(images); shared[1]["Id"] = shared[0]["Id"]
        for invalid in (images[:2], images + [images[0]], [images[0], images[0], images[2]], shared):
            with self.subTest(size=len(invalid)):
                with self.assertRaises(ValueError): labels.verify_labels(META, invalid)

    def test_wrong_tag_and_malformed_inspection_refused(self):
        for key, value in (("RepoTags", ["strataai-web:other"]), ("RepoTags", None), ("Id", "private-id"), ("Config", None)):
            images = inspection(); images[0][key] = value
            with self.subTest(key=key):
                with self.assertRaises(ValueError): labels.verify_labels(META, images)

    def test_cli_rejects_bad_provenance_without_echoing_private_input(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); images = inspection()
            images[0]["Config"]["Labels"]["org.opencontainers.image.source"] = "PRIVATE WRONG VALUE"
            (root / "metadata.json").write_text(json.dumps(META), encoding="utf-8")
            (root / "images.json").write_text(json.dumps(images), encoding="utf-8")
            result = subprocess.run([sys.executable, labels.__file__, "--metadata", str(root / "metadata.json"), "--images", str(root / "images.json")], capture_output=True)
            self.assertEqual(result.returncode, 1)
            self.assertNotIn(b"PRIVATE WRONG VALUE", result.stdout + result.stderr)
            self.assertEqual(result.stdout.strip(), b"Image provenance verification failed.")

    def test_cli_requires_the_canonical_metadata_shape(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); bad = copy.deepcopy(META); bad["workflowRunId"] = "PRIVATE WRONG RUN"
            (root / "metadata.json").write_text(json.dumps(bad), encoding="utf-8")
            (root / "images.json").write_text(json.dumps(inspection()), encoding="utf-8")
            result = subprocess.run([sys.executable, labels.__file__, "--metadata", str(root / "metadata.json"), "--images", str(root / "images.json")], capture_output=True)
            self.assertEqual(result.returncode, 1)
            self.assertNotIn(b"PRIVATE WRONG RUN", result.stdout + result.stderr)

    def test_cli_accepts_only_the_complete_matching_candidate(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "metadata.json").write_text(json.dumps(META), encoding="utf-8")
            (root / "images.json").write_text(json.dumps(inspection()), encoding="utf-8")
            result = subprocess.run([sys.executable, labels.__file__, "--metadata", str(root / "metadata.json"), "--images", str(root / "images.json")], capture_output=True)
            self.assertEqual(result.returncode, 0)
            self.assertIn(b"Exactly three image identities", result.stdout)


    def test_cli_retains_only_public_provenance_and_compares_loaded_ids(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); images = inspection()
            images[0]["Config"]["Env"] = ["PRIVATE FIXTURE VALUE"]
            (root / "metadata.json").write_text(json.dumps(META), encoding="utf-8")
            (root / "images.json").write_text(json.dumps(images), encoding="utf-8")
            args = [sys.executable, labels.__file__, "--metadata", str(root / "metadata.json"), "--images", str(root / "images.json")]
            self.assertEqual(subprocess.run(args + ["--output", str(root / "record.json")], capture_output=True).returncode, 0)
            record = json.loads((root / "record.json").read_text(encoding="utf-8"))
            self.assertEqual(set(record), {"schemaVersion", "build", "imageIds"})
            self.assertEqual(labels.verify_record(META, record), labels.verify_labels(META, images))
            self.assertNotIn("PRIVATE FIXTURE VALUE", (root / "record.json").read_text(encoding="utf-8"))
            self.assertEqual(subprocess.run(args + ["--expected", str(root / "record.json")], capture_output=True).returncode, 0)
            for index in range(3):
                changed = copy.deepcopy(images); changed[index]["Id"] = "sha256:" + "f" * 64
                (root / "images.json").write_text(json.dumps(changed), encoding="utf-8")
                result = subprocess.run(args + ["--expected", str(root / "record.json")], capture_output=True)
                self.assertEqual(result.returncode, 1)
                self.assertEqual(result.stdout.strip(), b"Image provenance verification failed.")

    def test_record_refuses_foreign_identity_duplicate_ids_and_extra_private_fields(self):
        record = {"schemaVersion": 1, "build": META, "imageIds": labels.verify_labels(META, inspection())}
        mutations = []
        for field, value in (("schemaVersion", True), ("schemaVersion", 2), ("private", "PRIVATE FIXTURE VALUE"), ("imageIds", {})):
            changed = copy.deepcopy(record); changed[field] = value; mutations.append(changed)
        changed = copy.deepcopy(record); changed["build"]["workflowRunId"] = "456"; mutations.append(changed)
        changed = copy.deepcopy(record); changed["imageIds"]["api"] = changed["imageIds"]["web"]; mutations.append(changed)
        for changed in mutations:
            with self.assertRaises(ValueError): labels.verify_record(META, changed)


def saved_image(root, host, inspected, transform=None):
    config = json.dumps({"architecture": "amd64", "os": "linux", "config": inspected["Config"],
                         "rootfs": {"type": "layers", "diff_ids": []}}, separators=(",", ":")).encode()
    config_id = "sha256:" + hashlib.sha256(config).hexdigest()
    manifest = json.dumps({"schemaVersion": 2, "mediaType": "application/vnd.oci.image.manifest.v1+json",
        "config": {"mediaType": "application/vnd.oci.image.config.v1+json", "digest": config_id, "size": len(config)}, "layers": []}, separators=(",", ":")).encode()
    manifest_id = "sha256:" + hashlib.sha256(manifest).hexdigest()
    descriptor = {"mediaType": "application/vnd.oci.image.manifest.v1+json", "digest": manifest_id, "size": len(manifest),
                  "annotations": {"io.containerd.image.name": "docker.io/library/" + META["images"][host],
                                  "org.opencontainers.image.ref.name": META["imageTag"]}}
    index = {"schemaVersion": 2, "manifests": [descriptor]}
    entries = [("index.json", json.dumps(index).encode()), ("blobs/sha256/" + manifest_id[7:], manifest),
               ("blobs/sha256/" + config_id[7:], config), ("oci-layout", b'{"imageLayoutVersion":"1.0.0"}'),
               ("manifest.json", json.dumps([{"Config": "blobs/sha256/" + config_id[7:],
                 "RepoTags": [META["images"][host]], "Layers": []}]).encode())]
    if transform: entries = transform(entries)
    with tarfile.open(root / f"strataai-{host}.tar.gz", "w:gz") as archive:
        for name, raw in entries:
            entry = tarfile.TarInfo(name); entry.size = len(raw); archive.addfile(entry, io.BytesIO(raw))
    return config_id, manifest_id, descriptor


class LoadedArchiveIdentityTests(unittest.TestCase):
    def fixture(self, root, reverse=False):
        images = inspection(); ids = {}
        # Each host needs a distinct config, as the real build record requires.
        for host, image in zip(labels.HOSTS, images):
            image["Config"]["Env"] = ["PUBLIC_FIXTURE_HOST=" + host]
            config_id, manifest_id, descriptor = saved_image(root, host, image)
            image["Id"] = config_id if reverse else manifest_id
            if not reverse: image["Descriptor"] = descriptor
            ids[host] = manifest_id if reverse else config_id
        return images, {"schemaVersion": 1, "build": META, "imageIds": ids}

    def test_classic_to_containerd_binding_preserves_original_record_and_inspection_order(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); images, expected = self.fixture(root)
            original = copy.deepcopy(expected)
            self.assertEqual(labels.verify_loaded_record(META, list(reversed(images)), expected, root), original)
            self.assertEqual(expected, original)

    def test_containerd_to_classic_binding_uses_the_same_saved_manifest_and_config(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); images, expected = self.fixture(root, reverse=True)
            self.assertEqual(labels.verify_loaded_record(META, images, expected, root), expected)

    def test_matching_labels_and_tags_do_not_authorize_id_conversion_without_archives(self):
        with tempfile.TemporaryDirectory() as folder:
            images, expected = self.fixture(Path(folder))
            with self.assertRaises(ValueError): labels.verify_loaded_record(META, images, expected)

    def test_missing_archive_and_different_saved_config_refuse_each_host(self):
        for host in labels.HOSTS:
            with self.subTest(host=host), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); images, expected = self.fixture(root)
                (root / f"strataai-{host}.tar.gz").unlink()
                with self.assertRaises((ValueError, KeyError)): labels.verify_loaded_record(META, images, expected, root)
                other = copy.deepcopy(next(image for image in images if META["images"][host] in image["RepoTags"]))
                other["Config"]["Env"] = ["PRIVATE DIFFERENT CONFIG"]
                saved_image(root, host, other)
                with self.assertRaises(ValueError): labels.verify_loaded_record(META, images, expected, root)

    def test_loaded_id_and_descriptor_cannot_be_substituted(self):
        for field, value in (("Id", "sha256:" + "f" * 64), ("Descriptor", None),
                ("Descriptor", {"digest": "sha256:" + "f" * 64}),
                ("Descriptor", {"digest": "sha256:" + "1" * 64, "mediaType": "PRIVATE TYPE", "size": 0})):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); images, expected = self.fixture(root); images[0][field] = value
                with self.assertRaises(ValueError): labels.verify_loaded_record(META, images, expected, root)

    def test_missing_foreign_and_ambiguous_index_refused(self):
        changes = [lambda entries: entries[1:], lambda entries: entries + [entries[0]],
                   lambda entries: [("index.json", b'{"schemaVersion":2,"manifests":[]}')] + entries[1:],
                   lambda entries: [("index.json", b'{"schemaVersion":2,"schemaVersion":2,"manifests":[]}')] + entries[1:]]
        for change in changes:
            with self.subTest(change=changes.index(change)), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); images, expected = self.fixture(root); saved_image(root, "web", images[0], change)
                with self.assertRaises((ValueError, KeyError)): labels.verify_loaded_record(META, images, expected, root)

    def test_modified_manifest_and_config_bytes_cannot_reuse_digest_names(self):
        for target in (1, 2):
            with self.subTest(target=target), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); images, expected = self.fixture(root)
                def change(entries):
                    entries[target] = (entries[target][0], entries[target][1] + b" ")
                    return entries
                saved_image(root, "web", images[0], change)
                with self.assertRaises(ValueError): labels.verify_loaded_record(META, images, expected, root)

    def test_archive_labels_cannot_differ_from_loaded_image_even_with_self_consistent_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); images, expected = self.fixture(root)
            other = copy.deepcopy(images[0]); other["Config"]["Labels"]["extra"] = "PRIVATE ARCHIVE LABEL"
            config_id, manifest_id, descriptor = saved_image(root, "web", other)
            images[0]["Id"] = manifest_id; images[0]["Descriptor"] = descriptor; expected["imageIds"]["web"] = config_id
            with self.assertRaises(ValueError): labels.verify_loaded_record(META, images, expected, root)

    def test_cli_conversion_retains_public_record_and_sanitizes_corrupt_archive_failure(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); images, expected = self.fixture(root)
            for name, data in (("metadata.json", META), ("images.json", images), ("expected.json", expected)):
                (root / name).write_text(json.dumps(data))
            args = [sys.executable, labels.__file__, "--metadata", str(root / "metadata.json"),
                    "--images", str(root / "images.json"), "--expected", str(root / "expected.json"),
                    "--archives", str(root), "--output", str(root / "verified.json")]
            result = subprocess.run(args, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads((root / "verified.json").read_text()), expected)
            (root / "strataai-web.tar.gz").write_bytes(b"PRIVATE CORRUPT ARCHIVE")
            (root / "verified.json").unlink()
            result = subprocess.run(args, capture_output=True)
            self.assertEqual(result.returncode, 1)
            self.assertNotIn(b"PRIVATE", result.stdout + result.stderr)
            self.assertFalse((root / "verified.json").exists())


if __name__ == "__main__": unittest.main()
