import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/record-release.py"
spec = importlib.util.spec_from_file_location("release_state", SCRIPT)
release = importlib.util.module_from_spec(spec); spec.loader.exec_module(release)


class ReleaseStateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="strataai-release-state-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.bundle = self.root / "bundle"
        (self.bundle / "images").mkdir(parents=True)
        self.state = self.root / "deployment.json"; self.checked = []
        self.candidate("a", "1")

    def candidate(self, digit, run):
        sha = digit * 40
        self.metadata = {"repository": "example/StrataAI2", "commitSha": sha, "shortSha": sha[:12],
                         "workflowRunId": run, "workflowRunNumber": run, "workflowRunAttempt": "1",
                         "version": "0.1.0-" + run, "releaseVersion": None, "imageTag": sha,
                         "createdAt": "2026-10-15T00:00:00.000Z",
                         "images": {host: "strataai-" + host + ":" + sha for host in release.HOSTS}}
        self.provenance = {"schemaVersion": 1, "build": self.metadata,
                           "imageIds": {host: "sha256:" + digit * 64 for host in release.HOSTS}}
        self.write_candidate()

    def write_candidate(self):
        (self.bundle / "build-metadata.json").write_text(json.dumps(self.metadata))
        (self.bundle / "images/image-provenance.json").write_text(json.dumps(self.provenance))

    def verify(self, expected):
        self.checked.append(expected)
        self.assertNotEqual(expected.parent, self.bundle)
        self.assertEqual(json.loads(expected.read_bytes()), self.metadata)

    def accept(self, initialize=False, **kwargs):
        return release.accept(self.bundle, self.state, kwargs.pop("environment", "staging"),
                              kwargs.pop("project", "strataai-staging"), initialize, kwargs.pop("verify", self.verify), **kwargs)

    def seed(self):
        self.assertEqual(self.accept(True), "recorded")
        return self.state.read_bytes()

    def assert_unchanged(self, before):
        self.assertEqual(self.state.read_bytes(), before)
        self.assertFalse(list(self.root.glob(".strataai-release-*")))

    def test_initialization_is_explicit_before_any_runtime_check(self):
        with self.assertRaises(ValueError): self.accept()
        self.assertFalse(self.state.exists()); self.assertEqual(self.checked, [])

    def test_first_acceptance_records_exact_build_and_image_ids(self):
        self.seed(); value = json.loads(self.state.read_bytes())
        self.assertEqual(value["current"]["build"], self.metadata)
        self.assertEqual(value["current"]["imageIds"], self.provenance["imageIds"])
        self.assertIsNone(value["previous"])
        self.assertEqual(len(self.checked), 1); self.assertFalse(self.checked[0].exists())

    def test_new_release_atomically_retains_complete_prior_release(self):
        self.seed(); first = json.loads(self.state.read_bytes())["current"]
        self.candidate("b", "2"); self.accept()
        value = json.loads(self.state.read_bytes())
        self.assertEqual(value["previous"], first)
        self.assertEqual(value["current"]["build"], self.metadata)
        self.assertEqual(value["current"]["imageIds"], self.provenance["imageIds"])

    def test_failed_runtime_preserves_both_known_good_references(self):
        self.seed(); self.candidate("b", "2"); self.accept(); before = self.state.read_bytes()
        self.candidate("c", "3")
        def fail(expected): raise ValueError("private-runtime-response")
        with self.assertRaises(ValueError): self.accept(verify=fail)
        self.assert_unchanged(before)

    def test_repeated_acceptance_rechecks_runtime_without_erasing_previous(self):
        self.seed(); self.candidate("b", "2"); self.accept(); before = self.state.read_bytes()
        self.assertEqual(self.accept(), "already-recorded"); self.assert_unchanged(before)
        self.assertEqual(len(self.checked), 3)

    def test_operator_rollback_can_record_original_images_without_rebuilding(self):
        self.seed(); original = json.loads(self.state.read_bytes())["current"]
        self.candidate("b", "2"); self.accept(); newer = json.loads(self.state.read_bytes())["current"]
        self.candidate("a", "1"); self.accept(); value = json.loads(self.state.read_bytes())
        self.assertEqual(value["previous"], newer)
        self.assertEqual(value["current"]["imageIds"], original["imageIds"])
        self.assertEqual(value["current"]["build"], original["build"])

    def test_initialization_cannot_reset_existing_history(self):
        before = self.seed()
        with self.assertRaises(ValueError): self.accept(True)
        self.assert_unchanged(before)

    def test_wrong_environment_or_project_refuses_before_runtime_check(self):
        before = self.seed(); self.checked.clear()
        for kwargs in [{"environment": "production"}, {"project": "other-staging"}]:
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError): self.accept(**kwargs)
            self.assert_unchanged(before)
        self.assertEqual(self.checked, [])

    def test_invalid_state_cannot_be_treated_as_first_deployment(self):
        for body in [b"null", b"{}", b"{", b'[]', b'{"schemaVersion":1,"schemaVersion":1}', b" " * 32769]:
            self.state.write_bytes(body)
            with self.subTest(body=body[:20]), self.assertRaises(ValueError): self.accept(True)
            self.assert_unchanged(body)

    def test_invalid_retained_snapshot_refuses_preserving_the_record(self):
        self.seed(); good = json.loads(self.state.read_bytes())
        for change in [lambda v: v["current"].pop("imageIds"), lambda v: v["current"].update(acceptedAt="not-a-clock"),
                       lambda v: v.update(previous={}), lambda v: v["current"].update(buildMetadataSha256="unverified")]:
            value = copy.deepcopy(good); change(value); self.state.write_text(json.dumps(value)); before = self.state.read_bytes()
            with self.assertRaises(ValueError): self.accept()
            self.assert_unchanged(before)

    def test_unexpected_secret_metadata_field_cannot_enter_the_record(self):
        self.metadata["privateSecret"] = "private-input"; self.write_candidate()
        with self.assertRaises(ValueError): self.accept(True)
        self.assertFalse(self.state.exists())

    def test_malformed_or_ambiguous_metadata_refuses_before_runtime_check(self):
        path = self.bundle / "build-metadata.json"
        for body in [b"null", b"{", b" " * 4097, b'{"commitSha":"a","commitSha":"b"}']:
            path.write_bytes(body)
            with self.subTest(body=body[:20]), self.assertRaises(ValueError): self.accept(True)
        self.assertEqual(self.checked, [])

    def test_provenance_must_match_selected_build_and_all_three_image_ids(self):
        good = copy.deepcopy(self.provenance)
        for change in [lambda v: v.update(schemaVersion=True), lambda v: v["build"].update(version="different"),
                       lambda v: v["imageIds"].pop("worker"), lambda v: v["imageIds"].update(api="different"),
                       lambda v: v.update(secret="private-input")]:
            self.provenance = copy.deepcopy(good); change(self.provenance); self.write_candidate()
            with self.assertRaises(ValueError): self.accept(True)
        self.assertEqual(self.checked, [])

    def test_candidate_changes_during_runtime_check_cannot_replace_prior(self):
        before = self.seed(); self.candidate("b", "2")
        for relative in ["build-metadata.json", "images/image-provenance.json"]:
            self.write_candidate()
            def changed(expected): self.verify(expected); (self.bundle / relative).write_text("{}")
            with self.subTest(relative=relative), self.assertRaises(ValueError): self.accept(verify=changed)
            self.assert_unchanged(before)

    def test_state_changed_by_noncooperating_writer_is_not_overwritten(self):
        self.seed(); self.candidate("b", "2")
        def changed(expected): self.state.write_bytes(b"operator-changed-record")
        with self.assertRaises(ValueError): self.accept(verify=changed)
        self.assert_unchanged(b"operator-changed-record")

    def test_failed_atomic_replace_preserves_prior_and_removes_temporary_file(self):
        before = self.seed(); self.candidate("b", "2")
        with patch.object(release.os, "replace", side_effect=OSError("private-filesystem-detail")):
            with self.assertRaises(OSError): self.accept()
        self.assert_unchanged(before)

    def test_competing_acceptance_refuses_before_runtime_or_state_change(self):
        before = self.seed(); self.checked.clear()
        with release.deployment_lock(self.state.with_name(self.state.name + ".lock")):
            with self.assertRaises(OSError): self.accept()
        self.assert_unchanged(before); self.assertEqual(self.checked, [])

    def test_operating_system_lock_is_reusable_after_failure(self):
        self.seed()
        with self.assertRaises(ValueError): self.accept(True)
        self.candidate("b", "2"); self.assertEqual(self.accept(), "recorded")

    def test_invalid_deployment_identity_never_invokes_runtime(self):
        for kwargs in [{"environment": "private/unsafe"}, {"project": "unsafe project"}]:
            with self.assertRaises(ValueError): self.accept(True, **kwargs)
        self.assertEqual(self.checked, [])

    def test_mutable_state_cannot_be_written_inside_immutable_bundle(self):
        self.state = self.bundle / "deployment.json"
        with self.assertRaises(ValueError): self.accept(True)
        self.assertFalse(self.state.exists()); self.assertEqual(self.checked, [])

    def test_runtime_failure_or_deadline_does_not_disclose_captured_output(self):
        for result in [subprocess.CompletedProcess([], 1, b"private-body", b"private-url"),
                       subprocess.TimeoutExpired("bash", 1, b"private-body", b"private-url")]:
            with patch.object(release.subprocess, "run", side_effect=result if isinstance(result, Exception) else None,
                              return_value=result):
                with self.assertRaises((ValueError, subprocess.TimeoutExpired)):
                    release.runtime_check(Path("expected.json"))

    def test_cli_failure_is_sanitized_and_keeps_prior_reference(self):
        before = self.seed()
        result = subprocess.run([sys.executable, str(SCRIPT), "--bundle", str(self.root / "private-missing-path"),
                                 "--state", str(self.state), "--environment", "staging", "--project", "strataai-staging"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1); self.assertNotIn("private-missing-path", result.stdout + result.stderr)
        self.assertNotIn("Traceback", result.stderr); self.assert_unchanged(before)
