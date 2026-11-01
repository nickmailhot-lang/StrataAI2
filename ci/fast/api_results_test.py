import tempfile
import unittest
from pathlib import Path
from api_results import validate

METHOD = "StrataAI.Api.Tests.ApiHostTests.Permission_matrix"
SELECTION = {"methods": [METHOD], "theoryRows": "all"}


def report(rows=2):
    results = ''.join(f'<UnitTestResult testId="t{i}" executionId="e{i}" outcome="Passed"/>' for i in range(rows))
    definitions = ''.join(f'<UnitTest id="t{i}"><TestMethod className="StrataAI.Api.Tests.ApiHostTests, Assembly" name="Permission_matrix"/></UnitTest>' for i in range(rows))
    return f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>{results}</Results><TestDefinitions>{definitions}</TestDefinitions><ResultSummary outcome="Completed"><Counters total="{rows}" executed="{rows}" passed="{rows}" failed="0" notExecuted="0"/></ResultSummary></TestRun>'''


class ApiResultsTests(unittest.TestCase):
    def check(self, xml, selection=None):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'result.trx'
            path.write_text(xml)
            return validate(selection or SELECTION, path)

    def test_all_theory_rows_are_counted_without_private_arguments(self):
        result = self.check(report().replace('outcome="Passed"', 'testName="PRIVATE_TOKEN" outcome="Passed"'))
        self.assertEqual(result['cases'], 2)
        self.assertEqual(result['methods'], 1)
        self.assertFalse(result['productionQualified'])
        self.assertNotIn('PRIVATE_TOKEN', str(result))

    def test_missing_method_or_empty_report_refuses(self):
        for xml, selection in [(report(0), SELECTION), (report(), {"methods": [METHOD, METHOD+'2'], "theoryRows": "all"})]:
            with self.assertRaises(ValueError): self.check(xml, selection)

    def test_failed_skipped_and_unknown_results_refuse(self):
        for outcome in ['Failed', 'NotExecuted', 'FutureOutcome']:
            with self.assertRaises(ValueError): self.check(report().replace('outcome="Passed"', f'outcome="{outcome}"', 1))

    def test_counter_or_incomplete_run_refuses(self):
        for old, new in [('passed="2"', 'passed="1"'), ('failed="0"', 'failed="1"'), ('outcome="Completed"', 'outcome="Aborted"')]:
            with self.assertRaises(ValueError): self.check(report().replace(old, new))

    def test_duplicate_execution_or_missing_definition_refuses(self):
        for old, new in [('executionId="e1"', 'executionId="e0"'), ('testId="t1"', 'testId="absent"'), ('id="t1"', 'id="t0"')]:
            with self.assertRaises(ValueError): self.check(report().replace(old, new))

    def test_unselected_methods_and_partial_rows_refuse(self):
        with self.assertRaises(ValueError): self.check(report().replace('name="Permission_matrix"', 'name="Unexpected"'))
        with self.assertRaises(ValueError): self.check(report(), {"methods": [METHOD], "theoryRows": "one"})

    def test_entity_declarations_refuse(self):
        with self.assertRaises(ValueError): self.check('<!DOCTYPE TestRun []>' + report())


if __name__ == '__main__': unittest.main()
