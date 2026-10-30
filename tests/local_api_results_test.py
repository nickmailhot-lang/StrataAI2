import importlib.util
import tempfile
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("api_results", Path(__file__).resolve().parents[1] / "scripts/local/api-test-results.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ApiBatchResultsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.inventory = {"methods": ["Tests.A", "Tests.B"], "batches": [["Tests.A"], ["Tests.B"]]}
        self.write(0, "A", "Pass")
        self.write(1, "B", "Skip")

    def write(self, index, method, status, extra=""):
        passed, skipped = int(status == "Pass"), int(status == "Skip")
        (self.root / f"batch-{index:03}.xml").write_text(f'<assemblies><assembly total="1" passed="{passed}" skipped="{skipped}" failed="0" errors="0" not-run="0"><collection><test type="Tests" method="{method}" result="{status}" />{extra}</collection></assembly></assemblies>')

    def test_complete_pass_and_existing_skip_remain_separate(self):
        result = MODULE.validate(self.inventory, self.root)
        self.assertEqual((result["total"], result["passed"], result["skipped"]), (2, 1, 1))

    def test_missing_result_refuses(self):
        (self.root / "batch-001.xml").unlink()
        with self.assertRaises(ValueError): MODULE.validate(self.inventory, self.root)

    def test_duplicate_method_across_batches_refuses(self):
        self.write(1, "A", "Pass")
        with self.assertRaises(ValueError): MODULE.validate(self.inventory, self.root)

    def test_inventory_partition_cannot_omit_a_method(self):
        self.inventory["batches"] = [["Tests.A"]]
        with self.assertRaises(ValueError): MODULE.validate(self.inventory, self.root)

    def test_unpassed_case_refuses(self):
        self.write(1, "B", "Fail")
        with self.assertRaises(ValueError): MODULE.validate(self.inventory, self.root)

    def test_inconsistent_case_count_refuses(self):
        self.write(1, "B", "Pass", '<test type="Tests" method="B" result="Pass" />')
        with self.assertRaises(ValueError): MODULE.validate(self.inventory, self.root)

    def test_unexpected_result_file_refuses(self):
        self.write(2, "C", "Pass")
        with self.assertRaises(ValueError): MODULE.validate(self.inventory, self.root)
