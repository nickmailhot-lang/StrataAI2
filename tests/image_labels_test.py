import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
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


if __name__ == "__main__": unittest.main()
