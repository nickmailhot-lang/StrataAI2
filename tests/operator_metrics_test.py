import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("operator_metrics", Path(__file__).resolve().parents[1] / "scripts/ci/verify-operator-metrics.py")
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)


class OperatorEvidenceTests(unittest.TestCase):
    revision = "a" * 40
    version = "0.1.0-1"

    def fixture(self):
        common = f'service_name="strataai-api",service_namespace="strataai",service_version="{self.version}",strataai_build_revision="{self.revision}"'
        observations = [("strataai_checklist_client_events", 'action="disclosure",kind="open"'),
                        ("strataai_checklist_client_events", 'action="create",kind="use"'),
                        ("strataai_checklist_client_events", 'action="create",kind="success"'),
                        ("strataai_checklist_client_duration_count", 'action="create",kind="success"'),
                        ("strataai_board_sharing_requests", 'operation="checklist_read",outcome="success"'),
                        ("strataai_board_sharing_duration_count", 'operation="checklist_read",outcome="success"'),
                        ("strataai_activity_client_events", 'action="card_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="board_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="card_read",kind="retry"'),
                        ("strataai_activity_client_events", 'action="card_read",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="card_read",kind="success"'),
                        ("strataai_board_sharing_requests", 'operation="card_activity_read",outcome="success"'),
                        ("strataai_board_sharing_requests", 'operation="board_activity_read",outcome="success"'),
                        ("strataai_activity_client_events", 'action="comment_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="comment_create",kind="use"'),
                        ("strataai_activity_client_events", 'action="comment_create",kind="success"'),
                        ("strataai_activity_client_events", 'action="mention_selection",kind="use"'),
                        ("strataai_activity_client_duration_count", 'action="comment_create",kind="success"'),
                        ("strataai_board_sharing_requests", 'operation="comment_create",outcome="success"'),
                        ("strataai_board_sharing_requests", 'operation="comment_read",outcome="success"')]
        return "\n".join(f'{name}{{{labels},{common}}} 1' for name, labels in observations)

    def test_complete_fixed_scope(self):
        self.assertTrue(metrics.verify(self.fixture(), self.revision, self.version))

    def test_missing_observation_or_wrong_build_cannot_pass(self):
        rows = self.fixture().splitlines()
        for index in range(len(rows)):
            self.assertFalse(metrics.verify("\n".join(rows[:index] + rows[index + 1:]), self.revision, self.version))
        self.assertFalse(metrics.verify(self.fixture(), "b" * 40, self.version))

    def test_protected_values_labels_and_unknown_metric_families_cannot_pass(self):
        for label in ['tenant_id="hidden"', 'instance="private-machine"', 'retry_key="opaque"']:
            self.assertFalse(metrics.verify(self.fixture().replace('action="create"', f'action="create",{label}'), self.revision, self.version))
        for value in ["private-metric-fixture", "00000000-0000-4000-8000-000000000001"]:
            self.assertFalse(metrics.verify(self.fixture() + "\n# " + value, self.revision, self.version))
        self.assertFalse(metrics.verify(self.fixture() + '\nstrataai_unknown_content{} 1', self.revision, self.version))
