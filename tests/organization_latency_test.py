import importlib.util
import math
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("organization_latency", Path(__file__).resolve().parents[1] / "scripts/ci/measure-organization-latency.py")
latency = importlib.util.module_from_spec(spec); spec.loader.exec_module(latency)


class OrganizationLatencyTests(unittest.TestCase):
    def test_nearest_rank_target_uses_unrounded_p95(self):
        values = list(range(1, 26))
        result = latency.summarize(values)
        self.assertEqual(13, result["p50Ms"]); self.assertEqual(24, result["p95Ms"])
        self.assertTrue(result["targetMet"])
        values[-2:] = [499.9999, 600]
        self.assertTrue(latency.summarize(values)["targetMet"])
        self.assertLess(latency.summarize(values)["p95Ms"], 500)
        values[-2] = 500
        self.assertFalse(latency.summarize(values)["targetMet"])

    def test_missing_nonfinite_or_negative_samples_cannot_pass(self):
        for values in ([1] * 24, [1] * 26, [1] * 24 + [math.inf], [1] * 24 + [math.nan], [1] * 24 + [-1]):
            with self.assertRaises(ValueError): latency.summarize(values)

    def test_full_field_payload_exercises_collection_limits_without_exceeding_payload_bound(self):
        values = latency.configuration(29, "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "33333333-3333-4333-8333-333333333333")
        import json
        self.assertEqual(18, len(values))
        for collection in ("emergencyContacts", "defaultCategories", "defaultPriorities", "jurisdictionPolicies"):
            self.assertEqual(32, len(values[collection]))
        self.assertLessEqual(len(json.dumps(values).encode()), 65536)
        self.assertGreater(len(json.dumps(values).encode()), 45000)

    def test_wrong_identity_or_topology_is_rejected_before_any_request(self):
        def unexpected(*_): self.fail("invalid identity contacted runtime")
        ids = ["11111111-1111-4111-8111-111111111111"] * 4
        with self.assertRaises(ValueError): latency.measure(unexpected, *ids, "invalid", "exact-release-images")
        with self.assertRaises(ValueError): latency.measure(unexpected, *ids, "a" * 40, "unknown")

    def test_wrong_actor_acknowledgment_does_not_produce_passing_report(self):
        organization = "11111111-1111-4111-8111-111111111111"
        actor = "22222222-2222-4222-8222-222222222222"
        def request(method, path, body=None, key=None):
            if method == "GET": return 200, {"version": 0}
            return 200, {"organizationId": organization, "actorId": organization, "version": 1, "configuration": body["configuration"]}
        with self.assertRaisesRegex(AssertionError, "configuration_acknowledgment_invalid"):
            latency.measure(request, organization, actor, actor, actor, "a" * 40, "local-compiled-runtime")
