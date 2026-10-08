import importlib.util
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("evidence_artifacts", Path(__file__).parents[1] / "scripts/ci/prepare-evidence-artifact.py")
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)
SHA = "a" * 40
META = {"repository": "example/StrataAI2", "commitSha": SHA, "shortSha": SHA[:12], "workflowRunId": "123", "workflowRunNumber": "1",
        "workflowRunAttempt": "1", "version": "0.1.0-1", "releaseVersion": None, "imageTag": SHA, "createdAt": "2026-10-08T00:00:00.000Z",
        "images": {host: f"strataai-{host}:{SHA}" for host in ("web", "api", "worker")}}


class EvidenceArtifactTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(prefix="strataai-evidence-")
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        self.metadata = self.root / "identity.json"
        self.raw = (json.dumps(META, indent=2) + "\n").encode()
        self.metadata.write_bytes(self.raw)
        self.output = self.root / "published"
        self.environment = patch.dict(os.environ, {}, clear=True)
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def stage(self, entries):
        return evidence.stage(self.metadata, self.output, entries)

    def test_exact_bytes_layout_checksums_and_unselected_private_files(self):
        source = self.root / "measurements"; source.mkdir()
        (source / "capacity.json").write_bytes(b'{"count":200}')
        (source / "private.env").write_bytes(b'PRIVATE_SECRET')
        self.assertTrue(self.stage([(source / "capacity.json", "capacity.json")]))
        self.assertEqual(sorted(p.name for p in self.output.iterdir()), ["SHA256SUMS", "build-metadata.json", "capacity.json"])
        self.assertEqual((self.output / "build-metadata.json").read_bytes(), self.raw)
        for line in (self.output / "SHA256SUMS").read_text().splitlines():
            digest, name = line.split("  ")
            self.assertEqual(digest, hashlib.sha256((self.output / name).read_bytes()).hexdigest())
        self.assertNotIn(b'PRIVATE_SECRET', b''.join(p.read_bytes() for p in self.output.iterdir()))

    def test_browser_directory_layout_preserved(self):
        source = self.root / "results"; (source / "case" / "attachments").mkdir(parents=True)
        (source / "case" / "attachments" / "trace.zip").write_bytes(b'trace')
        self.assertTrue(self.stage([(source, "test-results")]))
        self.assertEqual((self.output / "test-results/case/attachments/trace.zip").read_bytes(), b'trace')

    def test_missing_or_empty_evidence_does_not_publish_identity_only(self):
        empty = self.root / "empty"; empty.mkdir()
        self.assertFalse(self.stage([(self.root / "missing", "missing.json"), (empty, ".")]))
        self.assertFalse(self.output.exists())

    def test_identity_mismatches_refused_before_publication(self):
        source = self.root / "scope.json"; source.write_bytes(b'{}')
        for variable in ("GITHUB_SHA", "GITHUB_REPOSITORY", "GITHUB_RUN_ID", "STRATAAI_BUILD_VERSION"):
            with self.subTest(variable=variable), patch.dict(os.environ, {variable: "wrong"}):
                with self.assertRaises(ValueError): self.stage([(source, "scope.json")])
                self.assertFalse(self.output.exists())

    def test_unknown_metadata_fields_refused(self):
        self.metadata.write_text(json.dumps({**META, "secret": "PRIVATE_VALUE"}))
        with self.assertRaises(ValueError): self.stage([(self.root / "missing", "missing")])
        self.assertFalse(self.output.exists())

    def test_traversal_reserved_names_and_collisions_refused(self):
        source = self.root / "scope.json"; source.write_bytes(b'{}')
        for destination in ("../escape", "/absolute", "C:/absolute", "bad\\name", "bad\nname", "build-metadata.json", "SHA256SUMS"):
            with self.subTest(destination=destination):
                with self.assertRaises(ValueError): self.stage([(source, destination)])
                self.assertFalse(self.output.exists())
        with self.assertRaises(ValueError): self.stage([(source, "same.json"), (source, "same.json")])
        self.assertFalse(self.output.exists())

    def test_existing_output_is_never_overwritten(self):
        self.output.mkdir(); (self.output / "keep").write_bytes(b'original')
        source = self.root / "scope.json"; source.write_bytes(b'{}')
        with self.assertRaises(ValueError): self.stage([(source, "scope.json")])
        self.assertEqual((self.output / "keep").read_bytes(), b'original')

    def test_cli_failure_does_not_echo_private_metadata(self):
        self.metadata.write_text(json.dumps({**META, "secret": "PRIVATE_VALUE"}))
        result = subprocess.run([sys.executable, str(Path(evidence.__file__)), "--metadata", str(self.metadata), "--output", str(self.output), "--entry", str(self.root / "missing"), "missing"], capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn(b'PRIVATE_VALUE', result.stdout + result.stderr)
        self.assertFalse(self.output.exists())

    def test_hidden_browser_files_match_upload_exclusion(self):
        source = self.root / "results"; source.mkdir()
        (source / "trace.zip").write_bytes(b'trace')
        (source / ".last-run.json").write_bytes(b'PRIVATE_HIDDEN')
        (source / ".private").mkdir(); (source / ".private/token").write_bytes(b'PRIVATE_HIDDEN')
        self.assertTrue(self.stage([(source, ".")]))
        self.assertEqual(sorted(p.name for p in self.output.iterdir()), ["SHA256SUMS", "build-metadata.json", "trace.zip"])
        self.assertNotIn('last-run', (self.output / "SHA256SUMS").read_text())

    def test_copy_failure_never_publishes_partial_artifact(self):
        source = self.root / "scope.json"; source.write_bytes(b'{}')
        with patch.object(evidence.shutil, "copyfile", side_effect=OSError("PRIVATE_FAILURE")):
            with self.assertRaises(OSError): self.stage([(source, "scope.json")])
        self.assertFalse(self.output.exists())

    def test_symbolic_link_refused_without_reading_target(self):
        target = self.root / "private"; target.write_bytes(b'PRIVATE_SECRET')
        link = self.root / "linked"
        try: link.symlink_to(target)
        except OSError: self.skipTest("Symbolic links unavailable on this host")
        with self.assertRaises(ValueError): self.stage([(link, "scope.json")])
        self.assertFalse(self.output.exists())
