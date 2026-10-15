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
        observations = [("strataai_activity_client_events", 'action="organization_creation_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="organization_creation",kind="retry"'),
                        ("strataai_activity_client_events", 'action="organization_creation",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_creation",kind="success"'),
                        ("strataai_activity_client_events", 'action="organization_settings_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="organization_settings_read",kind="retry"'),
                        ("strataai_activity_client_events", 'action="organization_settings_update",kind="use"'),
                        ("strataai_activity_client_events", 'action="organization_settings_update",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_settings_update",kind="success"'),
                        ("strataai_organization_requests", 'operation="create",outcome="success"'),
                        ("strataai_organization_duration_count", 'operation="create",outcome="success"'),
                        ("strataai_organization_requests", 'operation="read",outcome="denied"'),
                        ("strataai_organization_duration_count", 'operation="read",outcome="denied"'),
                        ("strataai_checklist_client_events", 'action="disclosure",kind="open"'),
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
        for operation in ["configuration_read", "configuration_change", "configuration_history",
                          "configuration_activity", "configuration_replay", "operational_lifecycle_review",
                          "operational_lifecycle_directory", "operational_lifecycle_replay", "operational_lifecycle_change"]:
            for metric in ["strataai_organization_requests", "strataai_organization_duration_count"]:
                observations.append((metric, f'operation="{operation}",outcome="success"'))
        for metric in ["strataai_organization_requests", "strataai_organization_duration_count"]:
            observations.append((metric, 'operation="configuration_change",outcome="conflict",error_code="version_conflict"'))
            observations.append((metric, 'operation="configuration_read",outcome="denied"'))
        for action, kind in [("organization_configuration_disclosure", "open"), ("organization_configuration_read", "reconnect"),
                             ("organization_configuration_change", "retry"), ("organization_configuration_change", "failure"),
                             ("organization_configuration_change", "success"), ("organization_configuration_history", "success"),
                             ("organization_configuration_activity", "success"), ("organization_lifecycle_disclosure", "open"),
                               ("organization_lifecycle_read", "reconnect"), ("organization_lifecycle_change", "retry"),
                               ("organization_lifecycle_change", "conflict"), ("organization_lifecycle_change", "failure"),
                               ("organization_lifecycle_change", "success")]:
            observations.append(("strataai_activity_client_events", f'action="{action}",kind="{kind}"'))
            if kind in ("success", "failure"):
                observations.append(("strataai_activity_client_duration_count", f'action="{action}",kind="{kind}"'))
        for method, code in [("GET", "200"), ("POST", "400"), ("POST", "401"), ("POST", "403"), ("PATCH", "409")]:
            for metric in ["strataai_http_server_requests", "strataai_http_server_duration_count"]:
                observations.append((metric, f'method="{method}",status_code="{code}",outcome="completed"'))
        worker_common = common.replace('service_name="strataai-api"', 'service_name="strataai-worker"')
        return "\n".join(f'{name}{{{labels},{common}}} 1' for name, labels in observations) + \
            f'\nstrataai_worker_jobs{{queue="organization",outcome="completed",{worker_common}}} 1'

    def test_worker_requires_own_service_identity_and_bounded_queue_outcome(self):
        for original, replacement in [('queue="organization"', 'queue="private-job-type"'),
                                      ('queue="organization",outcome="completed"', 'queue="organization",outcome="sent"'),
                                      ('service_name="strataai-worker"', 'service_name="strataai-api"'),
                                      ('queue="organization"', 'queue="organization",actor_id="opaque"')]:
            with self.subTest(replacement=replacement):
                self.assertFalse(metrics.verify(self.fixture().replace(original, replacement), self.revision, self.version))

    def test_http_metrics_refuse_unbounded_labels_methods_statuses_and_outcomes(self):
        original = 'method="GET",status_code="200",outcome="completed"'
        for replacement in ['method="private-custom-method",status_code="200",outcome="completed"',
                            'method="GET",status_code="999",outcome="completed"',
                            'method="GET",status_code="200",outcome="private-failure"',
                            original + ',action="private-route"']:
            with self.subTest(replacement=replacement):
                self.assertFalse(metrics.verify(self.fixture().replace(original, replacement), self.revision, self.version))

    def test_complete_fixed_scope(self):
        self.assertTrue(metrics.verify(self.fixture(), self.revision, self.version))

    def test_duplicate_or_unparsed_labels_cannot_hide_private_values(self):
        original = 'queue="organization"'
        for replacement in ['queue="hidden-scope",queue="organization"',
                            'queue="organization",queue="organization"',
                            'queue="organization",unparsed-private-token',
                            'queue="organization" unparsed="hidden"',
                            'queue="organization",',
                            r'queue="organization",job="invalid\q"',
                            'queue="organization","private-scope"="hidden"']:
            with self.subTest(replacement=replacement):
                self.assertFalse(metrics.verify(self.fixture().replace(original, replacement), self.revision, self.version))

    def test_valid_label_whitespace_trailing_comma_and_quoted_names_preserve_fixed_scope(self):
        for fixture in [self.fixture().replace(',', ', '),
                        self.fixture().replace('} 1', ',} 1'),
                        self.fixture().replace('queue="organization"', '"queue"="organization"')]:
            with self.subTest(fixture=fixture[-150:]):
                self.assertTrue(metrics.verify(fixture, self.revision, self.version))

    def test_quoted_alias_cannot_duplicate_a_key_and_escaped_values_remain_intact(self):
        self.assertFalse(metrics.verify(self.fixture().replace('queue="organization"',
            '"queue"="hidden-scope",queue="organization"'), self.revision, self.version))
        escaped = r'path\\part\"quote\nnext'
        self.assertEqual({"job": escaped}, metrics.parse_labels('job="' + escaped + '"'))
        self.assertEqual({"job": "comma,inside"}, metrics.parse_labels('job="comma,inside",'))

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
