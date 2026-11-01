import unittest
import tempfile
import hashlib
from pathlib import Path
from domain_results import validate,RANK,LABEL


def report():
    methods=['StrataAI.Domain.Tests.Fixture.Original',RANK,RANK,LABEL]
    names=['original','capacity(count: 200)','capacity(count: 5000)','large references']
    definitions=''.join(f'<UnitTest id="t{i}"><TestMethod className="{m.rsplit(".",1)[0]}" name="{m.rsplit(".",1)[1]}"/></UnitTest>' for i,m in enumerate(methods))
    results=''.join(f'<UnitTestResult executionId="e{i}" testId="t{i}" testName="{n}" outcome="{"Passed" if i==0 else "NotExecuted"}"/>' for i,n in enumerate(names))
    return f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>{definitions}</TestDefinitions><Results>{results}</Results><ResultSummary outcome="Completed"><Counters total="4" passed="1" executed="1" failed="0" notExecuted="3"/></ResultSummary></TestRun>'


class DomainResultsTests(unittest.TestCase):
    def check(self,xml=None,changed_source=False):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);deferred={'sources':[]}
            for name in ['RankTokenTests.cs','CardLabelTransferPlanTests.cs']:
                source=root/'tests/StrataAI.Domain.Tests'/name;source.parent.mkdir(parents=True,exist_ok=True);source.write_text('original')
                deferred['sources'].append({'source':source.relative_to(root).as_posix(),'sha256':hashlib.sha256(source.read_bytes()).hexdigest()})
            if changed_source:source.write_text('changed')
            path=root/'domain.trx';path.write_text(xml or report())
            return validate(path,root,deferred)

    def test_original_three_deferrals_are_reported_separately(self):
        result=self.check();self.assertEqual(result['passed'],1);self.assertEqual(result['existingDeferred'],3);self.assertFalse(result['productionQualified'])

    def test_failed_or_new_skipped_case_refuses(self):
        for status in ['Failed','NotExecuted','Unknown']:
            with self.assertRaises(ValueError):self.check(report().replace('outcome="Passed"',f'outcome="{status}"'))

    def test_duplicate_execution_or_missing_capacity_row_refuses(self):
        with self.assertRaises(ValueError):self.check(report().replace('executionId="e2"','executionId="e1"'))
        with self.assertRaises(ValueError):self.check(report().replace('count: 5000','count: 200'))

    def test_changed_original_deferral_source_refuses(self):
        with self.assertRaises(ValueError):self.check(changed_source=True)

    def test_inconsistent_counts_or_incomplete_run_refuse(self):
        for old,new in [('executed="1"','executed="0"'),('notExecuted="3"','notExecuted="2"'),('outcome="Completed"','outcome="Aborted"')]:
            with self.assertRaises(ValueError):self.check(report().replace(old,new))


if __name__=='__main__':unittest.main()
