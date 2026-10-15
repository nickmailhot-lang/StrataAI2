import copy
import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("delivery", Path(__file__).resolve().parents[1] / "scripts/ci/test-operator-alert-delivery.py")
delivery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(delivery)


def payload(status="firing"):
    return {"receiver": "fixture-local", "status": status, "version": "4", "truncatedAlerts": 0,
            "routeLabels": {}, "notification_reason": "first notification" if status == "firing" else "all alerts resolved",
            "groupKey": '{}:{alertname="StrataAiCollectorUnavailable", component="metrics"}',
            "groupLabels": {"alertname": delivery.ALERT, "component": "metrics"},
            "commonLabels": dict(delivery.LABELS), "commonAnnotations": dict(delivery.ANNOTATIONS),
            "externalURL": "http://alertmanager:9093", "alerts": [{
                "status": status, "labels": dict(delivery.LABELS), "annotations": dict(delivery.ANNOTATIONS),
                "startsAt": "2026-10-15T00:00:00Z", "endsAt": "2026-10-15T00:03:00Z",
                "generatorURL": "http://prometheus:9090/graph?g0.expr=up&g0.tab=1", "fingerprint": "a" * 16}]}


class OperatorDeliveryTests(unittest.TestCase):
    def test_accepts_same_fixed_warning_firing_and_resolved(self):
        self.assertEqual(delivery.validate_notification(payload()), ("firing", "a" * 16))
        self.assertEqual(delivery.validate_notification(payload("resolved")), ("resolved", "a" * 16))

    def test_unknown_top_level_fields_are_not_retained(self):
        for key in ["recipient", "tenant_id", "token", "private_marker"]:
            changed = payload(); changed[key] = "private@example.test"
            with self.subTest(key=key), self.assertRaises(ValueError):
                delivery.validate_notification(changed)

    def test_private_or_unknown_labels_and_annotations_are_refused(self):
        for target in ["commonLabels", "commonAnnotations", "groupLabels", "routeLabels", "labels", "annotations"]:
            changed = payload()
            (changed if target.startswith("common") or target in {"groupLabels", "routeLabels"} else changed["alerts"][0])[target]["private_marker"] = "private@example.test"
            with self.subTest(target=target), self.assertRaises(ValueError):
                delivery.validate_notification(changed)

    def test_warning_cannot_change_identity_component_or_severity(self):
        for key, value in [("alertname", "OtherWarning"), ("component", "private@example.test"), ("severity", "critical")]:
            changed = payload(); changed["alerts"][0]["labels"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                delivery.validate_notification(changed)

    def test_unknown_alert_fields_and_extra_alerts_are_refused(self):
        changed = payload(); changed["alerts"][0]["recipient"] = "private@example.test"
        with self.assertRaises(ValueError): delivery.validate_notification(changed)
        changed = payload(); changed["alerts"].append(copy.deepcopy(changed["alerts"][0]))
        with self.assertRaises(ValueError): delivery.validate_notification(changed)

    def test_only_owned_receiver_and_backend_addresses_are_admitted(self):
        for field in ["receiver", "externalURL", "generatorURL"]:
            changed = payload()
            (changed["alerts"][0] if field == "generatorURL" else changed)[field] = "https://private@example.test/route"
            with self.subTest(field=field), self.assertRaises(ValueError):
                delivery.validate_notification(changed)

    def test_status_version_truncation_fingerprint_and_clocks_are_validated(self):
        for field, value in [("status", "pending"), ("version", "5"), ("truncatedAlerts", 1), ("notification_reason", "private@example.test"),
                             ("fingerprint", "private@example.test"), ("startsAt", "2026-10-15T00:00:00")]:
            changed = payload()
            (changed["alerts"][0] if field in {"fingerprint", "startsAt"} else changed)[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                delivery.validate_notification(changed)

    def test_revision_and_existing_output_refuse_before_docker_access(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(delivery.shutil, "which", side_effect=AssertionError("Docker must not be touched")):
            target = Path(temporary) / "result.json"
            for revision in ["main", "a" * 39, "A" * 40, "strataai-api:latest"]:
                with self.subTest(revision=revision), self.assertRaises(ValueError):
                    delivery.run(revision, target)
            target.write_text("existing")
            with self.assertRaises(ValueError): delivery.run("a" * 40, target)
            self.assertEqual(target.read_text(), "existing")

    def test_tool_size_and_digest_fail_before_extraction(self):
        for size in [2, 3]:
            opener = unittest.mock.Mock()
            opener.open.return_value = io.BytesIO(b"bad")
            with tempfile.TemporaryDirectory() as temporary, patch.object(delivery.urllib.request, "build_opener", return_value=opener), \
                    patch.object(delivery, "TOOLS", [("prometheus", "test", size, hashlib.sha256(b"yes").hexdigest())]), \
                    patch.object(delivery.tarfile, "open", side_effect=AssertionError("Unverified tool must not be opened")):
                with self.subTest(size=size), self.assertRaises(ValueError):
                    delivery.download_tools(Path(temporary))

    def test_tool_redirect_cannot_downgrade_to_http(self):
        with self.assertRaises(ValueError):
            delivery.HttpsRedirect().redirect_request(None, None, 302, "", {}, "http://example.test/tool")


if __name__ == "__main__":
    unittest.main()
