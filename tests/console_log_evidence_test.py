import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("console_evidence", Path(__file__).resolve().parents[1] / "scripts/ci/verify-console-logs.py")
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


class ConsoleEvidenceTests(unittest.TestCase):
    revision = "a" * 40
    version = "0.1.0-1"

    def record(self, component="strataai-api"):
        return {"Timestamp": "2026-10-15T12:00:00.1234567+00:00", "LogLevel": "Warning", "Component": component,
                "BuildRevision": self.revision, "BuildVersion": self.version, "Category": "Fixture.Operations",
                "EventId": 9, "EventName": "Observed", "CorrelationId": "private-reference_01",
                "OrganizationId": "00000000-0000-4000-8000-000000000001", "Message": "private-fixture-message", "ExceptionPresent": True}

    def verify(self, row):
        return evidence.verify(json.dumps(row), json.dumps(self.record("strataai-worker")), self.revision, self.version)

    def test_valid_records_publish_only_fixed_metadata_and_counts(self):
        result = self.verify(self.record())
        self.assertEqual({"strataai-api": 1, "strataai-worker": 1}, result["records"])
        self.assertNotIn("private", json.dumps(result))
        self.assertNotIn("00000000-0000", json.dumps(result))

    def test_every_field_is_required_and_raw_private_fields_are_refused(self):
        for field in evidence.FIELDS:
            row = self.record(); del row[field]
            with self.subTest(missing=field), self.assertRaises(ValueError): self.verify(row)
        for field in ["State", "Scopes", "Exception", "Password"]:
            row = self.record(); row[field] = "private-content"
            with self.subTest(extra=field), self.assertRaises(ValueError): self.verify(row)

    def test_build_service_and_utc_time_must_match(self):
        for field, value in [("Component", "strataai-worker"), ("BuildRevision", "b" * 40), ("BuildVersion", "0.2.0"),
                             ("Timestamp", "2026-10-15T12:00:00"), ("Timestamp", "2026-10-15T12:00:00+01:00")]:
            row = self.record(); row[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError): self.verify(row)

    def test_invalid_identifiers_severity_and_types_are_refused(self):
        for field, value in [("CorrelationId", "private value"), ("CorrelationId", "a" * 65), ("CorrelationId", 12),
                             ("OrganizationId", "private-name"), ("OrganizationId", "00000000-0000-0000-0000-000000000000"),
                             ("LogLevel", "private-severity"), ("EventId", True), ("EventName", []), ("Message", {}),
                             ("ExceptionPresent", "true"), ("Category", "")]:
            row = self.record(); row[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError): self.verify(row)

    def test_missing_context_and_null_event_name_are_valid(self):
        row = self.record(); row.update(CorrelationId=None, OrganizationId=None, EventName=None, ExceptionPresent=False)
        self.assertEqual("passed", self.verify(row)["status"])

    def test_plaintext_empty_non_object_and_duplicate_json_cannot_pass(self):
        for raw in ["", "info: private-message", "[]", "null", json.dumps(self.record()).replace('{', '{"Component":"private-hidden",', 1)]:
            with self.subTest(raw=raw[:30]), self.assertRaises((ValueError, TypeError)):
                evidence.verify(raw, json.dumps(self.record("strataai-worker")), self.revision, self.version)

    def test_escaped_message_stays_one_record_and_multiple_records_are_counted(self):
        row = self.record(); row["Message"] = 'private\n"quoted" 🙂'
        raw = json.dumps(row) + "\n" + json.dumps(row)
        self.assertEqual(2, evidence.verify(raw, json.dumps(self.record("strataai-worker")), self.revision, self.version)["records"]["strataai-api"])

    def test_cli_failure_is_generic_and_does_not_publish_or_overwrite(self):
        script = Path(__file__).resolve().parents[1] / "scripts/ci/verify-console-logs.py"
        with tempfile.TemporaryDirectory(prefix="strataai-console-evidence-") as folder:
            root = Path(folder); api = root/"api.log"; worker = root/"worker.log"; output = root/"evidence.json"
            api.write_text("private-invalid-content"); worker.write_text(json.dumps(self.record("strataai-worker")))
            args = [sys.executable, str(script), str(api), str(worker), self.revision, self.version, str(output)]
            result = subprocess.run(args, capture_output=True, text=True, check=False)
            self.assertEqual(1, result.returncode); self.assertEqual("", result.stdout)
            self.assertEqual("Console record verification failed.\n", result.stderr); self.assertFalse(output.exists())
            api.write_text(json.dumps(self.record())); output.write_text("retained")
            self.assertEqual(1, subprocess.run(args, capture_output=True, check=False).returncode)
            self.assertEqual("retained", output.read_text())
