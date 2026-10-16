import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("operator_metrics", Path(__file__).resolve().parents[1] / "scripts/ci/verify-operator-metrics.py")
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)


class OperatorEvidenceTests(unittest.TestCase):
    revision = "a" * 40
    version = "0.1.0-1"

    @staticmethod
    def pinned_jobs(raw):
        for service in ['strataai-api', 'strataai-worker']:
            raw = raw.replace(f'service_name="{service}"', f'job="strataai/{service}",service_name="{service}"')
        return raw

    def target_info(self, service='strataai-api'):
        return f'target_info{{job="strataai/{service}",service_version="{self.version}",strataai_build_revision="{self.revision}"}} 1'

    def test_optional_target_metadata_matches_pinned_resource_identity_in_both_modes(self):
        raw = self.pinned_jobs(self.fixture()) + '\n' + self.target_info() + '\n' + self.target_info('strataai-worker')
        self.assertTrue(metrics.verify(raw, self.revision, self.version))
        available = next(line for line in raw.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        self.assertTrue(metrics.verify(raw.replace(available, available[:-1] + '0'), self.revision, self.version, backlog_available=0))

    def test_target_metadata_cannot_hide_private_fields_values_or_wrong_builds(self):
        for service in ['strataai-api', 'strataai-worker']:
            line = self.target_info(service)
            invalid = [line.replace('job=', f'{label},job=') for label in [
                'recipient_email="private@example.test"', 'tenant_id="opaque"', 'host_name="private-host"',
                'process_command_line="private-command"', 'token="opaque"', 'telemetry_sdk_language="unexpected"',
                '"private_field"="opaque"']]
            invalid.extend([line.replace(f'strataai/{service}', value) for value in ['', 'private@example.test', service, 'strataai/other']])
            invalid.extend([line.replace(self.revision, 'b' * 40), line.replace(self.version, 'wrong-version'),
                            line.replace('} 1', '} 0'), line.replace('} 1', '} -1'), line.replace('} 1', '} NaN'),
                            line.replace('} 1', '} 1e309'), line.replace('job=', 'job="private",job='),
                            line.replace('job=', '"job"="private",job='),
                            line.replace('target_info{', 'target_info_private{'), 'target_info invalid'])
            for extra in invalid:
                with self.subTest(service=service, extra=extra):
                    self.assertFalse(metrics.verify(self.fixture() + '\n' + extra, self.revision, self.version))

    def test_target_metadata_requires_one_complete_record_per_reported_service(self):
        line = self.target_info()
        for field in [f'job="strataai/strataai-api",', f'service_version="{self.version}",', f',strataai_build_revision="{self.revision}"']:
            with self.subTest(field=field):
                self.assertFalse(metrics.verify(self.fixture() + '\n' + line.replace(field, ''), self.revision, self.version))
        self.assertFalse(metrics.verify(self.fixture() + '\n' + line + '\n' + line, self.revision, self.version))

    def test_target_metadata_accepts_prometheus_layout_but_not_whitespace_hidden_private_samples(self):
        line = self.target_info()
        for extra in [line.replace(',', ', '), line.replace('} 1', ',} 1'), line.replace('job=', '"job"='), '  '+line+'\t']:
            self.assertTrue(metrics.verify(self.fixture() + '\n' + extra, self.revision, self.version))
        self.assertFalse(metrics.verify(self.fixture() + '\n  ' + line.replace('job=', 'private="hidden",job='), self.revision, self.version))
        self.assertFalse(metrics.verify(self.fixture() + '\n  strataai_unknown{private="hidden"} 1', self.revision, self.version))

    def test_optional_collector_job_matches_its_service_in_healthy_and_unavailable_modes(self):
        raw = self.pinned_jobs(self.fixture())
        self.assertTrue(metrics.verify(raw, self.revision, self.version))
        # Omission remains supported; it cannot introduce an arbitrary label.
        self.assertTrue(metrics.verify(self.fixture(), self.revision, self.version))
        available = next(line for line in raw.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        unavailable = raw.replace(available, available[:-1] + '0')
        self.assertTrue(metrics.verify(unavailable, self.revision, self.version, backlog_available=0))

    def test_collector_job_cannot_carry_private_values_or_override_service_identity(self):
        for service, family in [('strataai-api', 'strataai_http_server_requests{'),
                                ('strataai-worker', 'strataai_worker_jobs{')]:
            raw = self.pinned_jobs(self.fixture())
            original = next(line for line in raw.splitlines() if line.startswith(family))
            other = 'strataai-worker' if service == 'strataai-api' else 'strataai-api'
            for value in ['', service, f'strataai/{other}', 'private-person@example.test', 'private-host.example.test',
                          f'strataai/{service}/private-suffix', f'STRATAAI/{service}', f'strataai/{service} ',
                          r'private\\host', r'private\nhost', 'private,inside', 'private/unicode-\u00e9']:
                bad = original.replace(f'job="strataai/{service}"', f'job="{value}"')
                changed = raw.replace(original, bad)
                available = next(line for line in changed.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
                with self.subTest(service=service, value=value):
                    self.assertFalse(metrics.verify(changed, self.revision, self.version))
                    self.assertFalse(metrics.verify(changed.replace(available, available[:-1] + '0'),
                                                    self.revision, self.version, backlog_available=0))

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
                        ("strataai_activity_client_events", 'action="organization_members_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="organization_members_read",kind="retry"'),
                        ("strataai_activity_client_events", 'action="organization_members_read",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_members_read",kind="success"'),
                        ("strataai_activity_client_events", 'action="organization_member_review",kind="use"'),
                        ("strataai_activity_client_events", 'action="organization_member_review",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_member_review",kind="success"'),
                        ("strataai_activity_client_events", 'action="organization_member_remove",kind="use"'),
                        ("strataai_activity_client_events", 'action="organization_member_remove",kind="retry"'),
                        ("strataai_activity_client_events", 'action="organization_member_remove",kind="conflict"'),
                        ("strataai_activity_client_events", 'action="organization_member_remove",kind="failure"'),
                        ("strataai_activity_client_duration_count", 'action="organization_member_remove",kind="failure"'),
                        ("strataai_activity_client_events", 'action="organization_member_remove",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_member_remove",kind="success"'),
                        ("strataai_activity_client_events", 'action="organization_departure_disclosure",kind="open"'),
                        ("strataai_activity_client_events", 'action="organization_departure_read",kind="retry"'),
                        ("strataai_activity_client_events", 'action="organization_departure_read",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_departure_read",kind="success"'),
                        ("strataai_activity_client_events", 'action="organization_departure_change",kind="use"'),
                        ("strataai_activity_client_events", 'action="organization_departure_change",kind="retry"'),
                        ("strataai_activity_client_events", 'action="organization_departure_change",kind="conflict"'),
                        ("strataai_activity_client_events", 'action="organization_departure_change",kind="failure"'),
                        ("strataai_activity_client_duration_count", 'action="organization_departure_change",kind="failure"'),
                        ("strataai_activity_client_events", 'action="organization_departure_change",kind="success"'),
                        ("strataai_activity_client_duration_count", 'action="organization_departure_change",kind="success"'),
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
        for operation, code in [("register", "201"), ("login", "200"), ("login", "401"), ("login", "403")]:
            for metric in ["strataai_authentication_requests", "strataai_authentication_duration_count"]:
                observations.append((metric, f'operation="{operation}",status_code="{code}",outcome="completed"'))
        for name, labels in list(observations):
            if name in {"strataai_http_server_duration_count", "strataai_authentication_duration_count"}:
                for bound in ['0.4', '0.75']:
                    observations.append((name.removesuffix('_count')+'_bucket', labels+f',le="{bound}"'))
        worker_common = common.replace('service_name="strataai-api"', 'service_name="strataai-worker"')
        return "\n".join(f'{name}{{{labels},{common}}} 1' for name, labels in observations) + \
            f'\nstrataai_worker_jobs{{queue="organization",outcome="completed",{worker_common}}} 1' + \
            ''.join(f'\nstrataai_worker_scoped_backlog{{queue="organization",state="{state}",{worker_common}}} 1' for state in ['pending', 'running']) + \
            f'\nstrataai_worker_scoped_backlog_available{{queue="organization",{worker_common}}} 1'

    def test_scoped_backlog_requires_both_nonzero_states_and_available_snapshot(self):
        for name, state in [('strataai_worker_scoped_backlog', 'pending'), ('strataai_worker_scoped_backlog', 'running'),
                            ('strataai_worker_scoped_backlog_available', None)]:
            with self.subTest(name=name, state=state):
                lines = self.fixture().splitlines()
                selected = next(line for line in lines if line.startswith(name + '{') and (state is None or f'state="{state}"' in line))
                self.assertFalse(metrics.verify('\n'.join(line for line in lines if line != selected), self.revision, self.version))
                self.assertFalse(metrics.verify(self.fixture().replace(selected, selected[:-1] + '0'), self.revision, self.version))

    def test_scoped_backlog_refuses_private_scope_invalid_state_and_invalid_counts(self):
        line = next(line for line in self.fixture().splitlines() if line.startswith('strataai_worker_scoped_backlog{'))
        invalid = [line.replace('state="pending"', label) for label in [
            'state="private-job-type"', 'state="pending",organization_id="opaque"',
            'state="pending",state="running"', 'outcome="completed"', 'state="pending",queue="identity_email"']]
        invalid.extend(line[:-1] + value for value in ['-1', '0.5', 'NaN', '1e309'])
        available = next(line for line in self.fixture().splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        invalid.append(available[:-1] + '2')
        invalid.append(available.replace('queue="organization"', 'queue="organization",state="pending"'))
        for observation in invalid:
            with self.subTest(observation=observation):
                self.assertFalse(metrics.verify(self.fixture() + '\n' + observation, self.revision, self.version))

    def test_unavailable_backlog_requires_current_zero_and_never_accepts_stale_healthy_availability(self):
        raw = self.fixture()
        available = next(line for line in raw.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        failed = raw.replace(available, available[:-1] + '0')
        self.assertTrue(metrics.verify(failed, self.revision, self.version, backlog_available=0))
        self.assertFalse(metrics.verify(failed, self.revision, self.version))
        self.assertFalse(metrics.verify(raw, self.revision, self.version, backlog_available=0))
        # SDK/Collector gauge retention can preserve old counts: availability
        # zero makes those counts unusable. Missing counts also remain valid.
        missing = '\n'.join(line for line in failed.splitlines() if not line.startswith('strataai_worker_scoped_backlog{'))
        self.assertTrue(metrics.verify(missing, self.revision, self.version, backlog_available=0))
        self.assertFalse(metrics.verify(missing, self.revision, self.version))

    def test_backlog_availability_rejects_duplicate_and_mixed_current_samples(self):
        raw = self.fixture()
        available = next(line for line in raw.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        for suffix in [available, available[:-1] + '0']:
            self.assertFalse(metrics.verify(raw + '\n' + suffix, self.revision, self.version))
            self.assertFalse(metrics.verify(raw + '\n' + suffix, self.revision, self.version, backlog_available=0))

    def test_unavailable_backlog_does_not_weaken_other_observations_or_private_field_refusal(self):
        raw = self.fixture()
        available = next(line for line in raw.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        failed = raw.replace(available, available[:-1] + '0')
        for invalid in [failed.replace('strataai_worker_jobs', 'unknown_worker_family'),
                        failed.replace('method="GET"', 'method="private-method"'),
                        failed.replace('queue="organization"', 'queue="organization",organization_id="private"'),
                        failed.replace(self.revision, 'b' * 40)]:
            self.assertFalse(metrics.verify(invalid, self.revision, self.version, backlog_available=0))
        for expected in [-1, 2, None, '0', False]:
            self.assertFalse(metrics.verify(failed, self.revision, self.version, backlog_available=expected))

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

    def test_authentication_observations_require_fixed_operation_status_and_private_field_exclusion(self):
        raw = self.fixture()
        original = next(line for line in raw.splitlines() if line.startswith('strataai_authentication_requests{'))
        for changed in [original.replace('operation="register"', 'operation="private-path"'),
                        original.replace('operation="register"', 'operation="register",email="private@example.test"'),
                        original.replace('operation="register"', 'operation="register",method="POST"'),
                        original.replace('status_code="201"', 'status_code="999"'),
                        original.replace('outcome="completed"', 'outcome="private-reason"'),
                        original.replace('service_name="strataai-api"', 'service_name="strataai-worker"'),
                        original[:-1]+'-1', original[:-1]+'NaN', original[:-1]+'1e309', original[:-1]+'e']:
            with self.subTest(changed=changed):
                self.assertFalse(metrics.verify(raw+'\n'+changed, self.revision, self.version))

    def test_authentication_histogram_bounds_are_numeric_and_cannot_be_private_labels(self):
        line = next(line for line in self.fixture().splitlines() if line.startswith('strataai_authentication_requests{'))
        bucket = line.replace('strataai_authentication_requests', 'strataai_authentication_duration_bucket')
        for boundary in ['0', '0.5', '+Inf']:
            self.assertTrue(metrics.verify(self.fixture()+'\n'+bucket.replace('operation=', f'le="{boundary}",operation='), self.revision, self.version))
        for boundary in ['private@example.test', '', 'NaN', '1e309']:
            self.assertFalse(metrics.verify(self.fixture()+'\n'+bucket.replace('operation=', f'le="{boundary}",operation='), self.revision, self.version))
        self.assertFalse(metrics.verify(self.fixture()+'\n'+bucket, self.revision, self.version))

    def test_authentication_nonbucket_metrics_cannot_hide_data_in_a_bucket_label(self):
        line = next(line for line in self.fixture().splitlines() if line.startswith('strataai_authentication_requests{'))
        for name in ['strataai_authentication_requests', 'strataai_authentication_duration_count', 'strataai_authentication_duration_sum']:
            extra = line.replace('strataai_authentication_requests', name).replace('operation=', 'le="private-value",operation=')
            self.assertFalse(metrics.verify(self.fixture()+'\n'+extra, self.revision, self.version))

    def test_each_server_threshold_boundary_is_required_even_with_complete_counts(self):
        raw = self.fixture()
        buckets = [line for line in raw.splitlines() if line.startswith(('strataai_http_server_duration_bucket{', 'strataai_authentication_duration_bucket{'))]
        self.assertEqual(len(buckets), 18)
        for line in buckets:
            with self.subTest(line=line):
                self.assertFalse(metrics.verify(raw.replace(line, ''), self.revision, self.version))
                self.assertFalse(metrics.verify(raw.replace(line, line.replace('le="0.', 'le="5.')), self.revision, self.version))

    def test_zero_threshold_buckets_are_not_a_latency_failure(self):
        raw = '\n'.join(line[:-1]+'0' if '_duration_bucket{' in line else line for line in self.fixture().splitlines())
        self.assertTrue(metrics.verify(raw, self.revision, self.version))

    def test_http_bounds_cannot_carry_private_or_nonfinite_values_or_label_non_buckets(self):
        raw = self.fixture()
        line = next(line for line in raw.splitlines() if line.startswith('strataai_http_server_requests{'))
        for suffix in ['requests', 'duration_count', 'duration_sum']:
            extra = line.replace('strataai_http_server_requests', 'strataai_http_server_'+suffix).replace('method=', 'le="private-value",method=')
            self.assertFalse(metrics.verify(raw+'\n'+extra, self.revision, self.version))
        bucket = line.replace('strataai_http_server_requests', 'strataai_http_server_duration_bucket')
        for bound in ['', 'private-value', 'NaN', '-Inf', '1e309']:
            extra = bucket.replace('method=', f'le="{bound}",method=')
            self.assertFalse(metrics.verify(raw+'\n'+extra, self.revision, self.version))

    def test_complete_fixed_scope(self):
        self.assertTrue(metrics.verify(self.fixture(), self.revision, self.version))

    def identity_observation(self, available=1):
        line = next(line for line in self.fixture().splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        line = line.replace('strataai_worker_scoped_backlog_available', 'strataai_worker_identity_backlog_available').replace('queue="organization"', 'queue="identity_email"')
        return '\n'.join([line[:-1] + str(available)] + [
            line.replace('strataai_worker_identity_backlog_available', 'strataai_worker_identity_backlog').replace('queue="identity_email"', f'queue="identity_email",state="{state}"')[:-1] + str(count)
            for state, count in [('pending', 0), ('running', 2)]])

    def test_identity_backlog_is_optional_and_requires_complete_healthy_observation(self):
        self.assertTrue(metrics.verify(self.fixture(), self.revision, self.version))
        extra = self.identity_observation()
        self.assertTrue(metrics.verify(self.pinned_jobs(self.fixture() + '\n' + extra), self.revision, self.version))
        for selected in extra.splitlines():
            missing = '\n'.join(line for line in extra.splitlines() if line != selected)
            self.assertFalse(metrics.verify(self.fixture() + '\n' + missing, self.revision, self.version))

    def test_identity_backlog_unavailable_accepts_no_or_retained_counts_but_not_duplicates(self):
        extra = self.identity_observation(0)
        for observation in [extra, extra.splitlines()[0]]:
            self.assertTrue(metrics.verify(self.fixture() + '\n' + observation, self.revision, self.version))
        for duplicate in extra.splitlines():
            self.assertFalse(metrics.verify(self.fixture() + '\n' + extra + '\n' + duplicate, self.revision, self.version))

    def test_identity_and_organization_observation_failures_are_independent(self):
        raw = self.fixture() + '\n' + self.identity_observation(0)
        scoped = next(line for line in raw.splitlines() if line.startswith('strataai_worker_scoped_backlog_available{'))
        raw = self.pinned_jobs(raw.replace(scoped, scoped[:-1] + '0'))
        self.assertTrue(metrics.verify(raw, self.revision, self.version, backlog_available=0))
        self.assertFalse(metrics.verify(raw, self.revision, self.version))

    def test_identity_backlog_rejects_private_fields_queue_state_and_counts(self):
        extra = self.identity_observation()
        for changed in [extra.replace('identity_email', 'organization'), extra.replace('state="pending"', 'state="private-purpose"'),
                        extra.replace('queue="identity_email"', 'queue="identity_email",recipient_email="private@example.test"'),
                        extra.replace('queue="identity_email"', 'queue="identity_email",purpose="RESET_PASSWORD"'),
                        extra.replace('state="running"', 'state="running",job_id="opaque"'),
                        extra.replace('} 2', '} -1'), extra.replace('} 2', '} 0.5'), extra.replace('} 2', '} NaN'),
                        extra.replace('} 2', '} 1e309'), extra.replace('available{', 'available{state="pending",'),
                        extra.replace(self.revision, 'b' * 40), extra.replace('service_name="strataai-worker"', 'service_name="strataai-api"')]:
            with self.subTest(changed=changed):
                self.assertFalse(metrics.verify(self.fixture() + '\n' + changed, self.revision, self.version))

    def loop_failure(self):
        return next(line for line in self.fixture().splitlines() if line.startswith('strataai_worker_jobs{')).replace('strataai_worker_jobs', 'strataai_worker_loop_failures').replace(',outcome="completed"', '')

    def test_worker_loop_failure_is_optional_and_has_only_fixed_queue_identity(self):
        self.assertTrue(metrics.verify(self.fixture(), self.revision, self.version))
        for queue in ['organization', 'identity_email']:
            with self.subTest(queue=queue):
                line = self.loop_failure().replace('queue="organization"', f'queue="{queue}"')
                self.assertTrue(metrics.verify(self.fixture() + '\n' + line, self.revision, self.version))

    def test_worker_loop_failure_rejects_private_fields_wrong_build_and_invalid_values(self):
        line = self.loop_failure()
        invalid = [line.replace('queue="organization"', label) for label in [
            'queue="private-queue"', '', 'queue="organization",outcome="failed"',
            'queue="organization",exception="private-provider-message"',
            'queue="organization",organization_id="opaque"',
            'queue="organization",queue="identity_email"']]
        invalid.extend([line.replace('service_name="strataai-worker"', 'service_name="strataai-api"'),
                        line.replace(self.revision, 'b' * 40), line.replace(self.version, 'wrong-version'),
                        line[:-1] + '-1', line[:-1] + 'NaN', line[:-1] + '1e309'])
        for observation in invalid:
            with self.subTest(observation=observation):
                self.assertFalse(metrics.verify(self.fixture() + '\n' + observation, self.revision, self.version))

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

    def test_missing_departure_observations_cannot_pass(self):
        for action in ('organization_departure_disclosure', 'organization_departure_read', 'organization_departure_change'):
            with self.subTest(action=action):
                rows = [row for row in self.fixture().splitlines() if f'action="{action}"' not in row]
                self.assertFalse(metrics.verify('\n'.join(rows), self.revision, self.version))

    def test_missing_member_observations_cannot_pass(self):
        for action in ('organization_members_disclosure', 'organization_members_read', 'organization_member_review', 'organization_member_remove'):
            with self.subTest(action=action):
                rows = [row for row in self.fixture().splitlines() if f'action="{action}"' not in row]
                self.assertFalse(metrics.verify('\n'.join(rows), self.revision, self.version))

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
