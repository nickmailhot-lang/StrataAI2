import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
import subprocess
import sys

spec = importlib.util.spec_from_file_location("source_reports", Path(__file__).parents[1] / "scripts/ci/summarize-source-tests.py")
reports = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reports)
SHA = "a" * 40
META = {"repository": "example/StrataAI2", "commitSha": SHA, "shortSha": SHA[:12], "workflowRunId": "123", "workflowRunNumber": "1",
        "workflowRunAttempt": "1", "version": "0.1.0-1", "releaseVersion": None, "imageTag": SHA, "createdAt": "2026-10-08T00:00:00.000Z",
        "images": {host: f"strataai-{host}:{SHA}" for host in ("web", "api", "worker")}}


class SourceReportTests(unittest.TestCase):
    def run_report(self, xml, kind="junit"):
        with tempfile.TemporaryDirectory(prefix="strataai-source-reports-") as folder:
            path = Path(folder) / "report.xml"
            path.write_text(xml, encoding="utf-8")
            return reports.summarize([path], kind, META)

    def test_junit_outcomes_and_private_content_exclusion(self):
        xml = '''<testsuites tests="4"><testsuite><testcase classname="src/features/auth/ProfilePage.test.tsx" name="PRIVATE_BEARER" time="0.012"/>
        <testcase classname="src/features/auth/ProfilePage.test.tsx" name="PRIVATE_PASSWORD" time="0.2"><failure message="PRIVATE_BODY">PRIVATE_DOM</failure><system-out>PRIVATE_PROVIDER</system-out></testcase>
        <testcase classname="private@example.test" name="error"><error>PRIVATE_STACK</error></testcase><testcase name="skip"><skipped/></testcase></testsuite></testsuites>'''
        summary, refused = self.run_report(xml)
        self.assertFalse(refused)
        report = summary["reports"][0]
        self.assertEqual(report["counts"], {"passed": 1, "failed": 1, "skipped": 1, "error": 1})
        self.assertEqual(report["cases"][0]["durationMs"], 12)
        self.assertEqual(report["cases"][0]["source"], "src/features/auth/ProfilePage.test.tsx")
        self.assertNotIn("PRIVATE_", json.dumps(summary))
        self.assertNotIn("private@example.test", json.dumps(summary))
        self.assertEqual(summary["build"], META)

    def test_trx_source_method_and_private_output_exclusion(self):
        xml = '''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
        <UnitTestResult testId="test1" testName="PRIVATE_THEORY_ARGUMENT" outcome="Failed" duration="00:00:01.250"><Output><ErrorInfo><Message>PRIVATE_TOKEN</Message></ErrorInfo></Output></UnitTestResult>
        <UnitTestResult testId="test2" outcome="Passed" duration="00:00:00.001"/>
        <UnitTestResult testId="test3" outcome="NotExecuted"/><UnitTestResult testId="test4" outcome="FutureOutcome"/>
        </Results><TestDefinitions><UnitTest id="test1"><TestMethod className="StrataAI.Api.Tests.IdentityTests, Assembly" name="Revocation_refuses_stale_session"/></UnitTest></TestDefinitions>
        <ResultSummary><Counters total="4"/></ResultSummary></TestRun>'''
        summary, refused = self.run_report(xml, "trx")
        self.assertFalse(refused)
        self.assertEqual(summary["reports"][0]["counts"], {"passed": 1, "failed": 1, "skipped": 1, "error": 1})
        self.assertEqual(summary["reports"][0]["cases"][0]["source"], "StrataAI.Api.Tests.IdentityTests.Revocation_refuses_stale_session")
        self.assertEqual(summary["reports"][0]["cases"][0]["durationMs"], 1250)
        self.assertNotIn("PRIVATE_", json.dumps(summary))

    def test_missing_report_is_explicit_and_refused(self):
        with tempfile.TemporaryDirectory(prefix="strataai-missing-reports-") as folder:
            summary, refused = reports.summarize([Path(folder) / "missing.xml"], "junit", META)
            self.assertTrue(refused)
            self.assertEqual(summary["reports"][0]["status"], "missing")
            self.assertEqual(summary["reports"][0]["cases"], [])

    def test_empty_and_malformed_reports_are_refused(self):
        for xml in ("<testsuites tests='0'/>", "<testcase/>", "<broken", "<testsuites tests='2'><testcase/></testsuites>", "<testsuites tests='1' failures='1'><testcase/></testsuites>", "<testsuites><testcase time='NaN'/></testsuites>", "<testsuites><testcase><skipped/></testcase></testsuites>"):
            with self.subTest(xml=xml):
                summary, refused = self.run_report(xml)
                self.assertTrue(refused)
                self.assertEqual(summary["reports"][0]["status"], "invalid")

    def test_trx_counters_cannot_advertise_unrecorded_results(self):
        xml = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult outcome="Passed"/></Results><ResultSummary><Counters total="2"/></ResultSummary></TestRun>'
        summary, refused = self.run_report(xml, "trx")
        self.assertTrue(refused)
        self.assertEqual(summary["reports"][0]["status"], "invalid")

    def test_duplicate_cases_are_retained_not_deduplicated(self):
        summary, refused = self.run_report('<testsuites tests="2"><testcase name="same"/><testcase name="same"><failure/></testcase></testsuites>')
        self.assertFalse(refused)
        self.assertEqual(summary["reports"][0]["counts"]["failed"], 1)
        self.assertEqual(len({row["caseId"] for row in summary["reports"][0]["cases"]}), 2)

    def test_identity_unknown_fields_and_output_injection_are_refused(self):
        for metadata in ({**META, "secret": "PRIVATE_TOKEN"}, {**META, "version": "0.1.0\nPRIVATE_TOKEN"}, {**META, "imageTag": "different"}):
            with self.assertRaises(ValueError):
                reports.build_identity(metadata)

    def test_cli_preserves_failure_exit_and_emits_only_the_summary(self):
        with tempfile.TemporaryDirectory(prefix="strataai-report-cli-") as folder:
            directory = Path(folder)
            (directory / "identity.json").write_text(json.dumps(META), encoding="utf-8")
            (directory / "raw.xml").write_text('<testsuites tests="1"><testcase name="PRIVATE_BEARER"><failure>PRIVATE_ASSERTION</failure></testcase></testsuites>', encoding="utf-8")
            command = [sys.executable, str(Path(reports.__file__)), "--format", "junit", "--metadata", str(directory / "identity.json"),
                       "--output", str(directory / "published/summary.json"), str(directory / "raw.xml")]
            environment = {**os.environ, "GITHUB_SHA": SHA, "GITHUB_REPOSITORY": META["repository"], "GITHUB_RUN_ID": META["workflowRunId"]}
            result = subprocess.run(command, capture_output=True, text=True, env=environment)
            self.assertEqual(result.returncode, 1)
            self.assertNotIn("PRIVATE_", result.stdout + result.stderr)
            emitted = json.loads((directory / "published/summary.json").read_text(encoding="utf-8"))
            self.assertEqual(emitted["reports"][0]["counts"]["failed"], 1)
            self.assertEqual([file.name for file in (directory / "published").iterdir()], ["summary.json"])
            self.assertNotIn("PRIVATE_", json.dumps(emitted))
            command[command.index("--output") + 1] = str(directory / "wrong-run/summary.json")
            wrong = subprocess.run(command, capture_output=True, text=True, env={**environment, "GITHUB_RUN_ID": "999"})
            self.assertEqual(wrong.returncode, 1)
            self.assertFalse((directory / "wrong-run/summary.json").exists())


if __name__ == "__main__":
    unittest.main()
