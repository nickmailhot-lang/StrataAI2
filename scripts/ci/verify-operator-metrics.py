"""Validate real Collector output; retain only fixed verification metadata."""
import json
import math
import re
import sys
from pathlib import Path


def verify(raw, revision, version):
    if not re.fullmatch(r"[0-9a-f]{40,64}", revision):
        return False
    if "private-metric-fixture" in raw or re.search(r"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}", raw, re.I):
        return False
    allowed = {"action", "kind", "operation", "outcome", "error_code", "keyed_attempt", "le", "job", "instance",
               "service_name", "service_namespace", "service_version", "strataai_build_revision"}
    samples = []
    names = {"strataai_checklist_client_events", "strataai_activity_client_events", "strataai_board_sharing_requests", "strataai_organization_requests"}
    names.update(prefix + suffix for prefix in ["strataai_checklist_client_duration", "strataai_activity_client_duration", "strataai_board_sharing_duration", "strataai_organization_duration"]
                 for suffix in ["_bucket", "_count", "_sum"])
    names.add("strataai_http_server_requests")
    names.update("strataai_http_server_duration" + suffix for suffix in ["_bucket", "_count", "_sum"])
    for line in raw.splitlines():
        if not line.startswith("strataai_"):
            continue
        match = re.fullmatch(r'(\w+)\{([^}]*)\} ([0-9.eE+\-]+)', line)
        if not match or match[1] not in names:
            return False
        labels = dict(re.findall(r'(\w+)="((?:\\.|[^"\\])*)"', match[2]))
        permitted = allowed | {"method", "status_code"} if match[1].startswith("strataai_http_server_") else allowed
        if set(labels) - permitted or labels.get("instance", "") != "":
            return False
        if labels.get("strataai_build_revision") != revision or labels.get("service_version") != version:
            return False
        if labels.get("service_name") != "strataai-api" or labels.get("service_namespace") != "strataai":
            return False
        if match[1].startswith("strataai_http_server_"):
            http_labels = {"method", "status_code", "outcome", "le", "job", "instance",
                           "service_name", "service_namespace", "service_version", "strataai_build_revision"}
            if set(labels) - http_labels or labels.get("method") not in {
                    "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "CONNECT", "TRACE", "OTHER"}:
                return False
            if not re.fullmatch(r"0|[1-5][0-9]{2}", labels.get("status_code", "")) or labels.get("outcome") not in {"completed", "cancelled", "faulted"}:
                return False
            if not math.isfinite(float(match[3])) or float(match[3]) < 0:
                return False
        samples.append((match[1], labels, float(match[3])))

    def observed(name, expected):
        return any(metric == name and (value == 1 if name.startswith(("strataai_checklist_client_", "strataai_activity_client_")) else value >= 1)
                   and all(labels.get(key) == val for key, val in expected.items())
                   for metric, labels, value in samples)

    return all([
        *[observed(metric, {"method": method, "status_code": code, "outcome": "completed"})
          for method, code in [("GET", "200"), ("POST", "400"), ("POST", "401"), ("POST", "403"), ("PATCH", "409")]
          for metric in ["strataai_http_server_requests", "strataai_http_server_duration_count"]],
        *[observed("strataai_activity_client_events", {"action": action, "kind": kind})
          for action, kind in [("organization_configuration_disclosure", "open"), ("organization_configuration_read", "reconnect"),
                               ("organization_configuration_change", "retry"), ("organization_configuration_change", "failure"),
                               ("organization_configuration_change", "success"), ("organization_configuration_history", "success"),
                               ("organization_configuration_activity", "success"), ("organization_lifecycle_disclosure", "open"),
                               ("organization_lifecycle_read", "reconnect"), ("organization_lifecycle_change", "retry"),
                               ("organization_lifecycle_change", "conflict"), ("organization_lifecycle_change", "failure"),
                               ("organization_lifecycle_change", "success")]],
        *[observed("strataai_activity_client_duration_count", {"action": action, "kind": kind})
          for action, kind in [("organization_configuration_change", "failure"), ("organization_configuration_change", "success"),
                               ("organization_configuration_history", "success"), ("organization_configuration_activity", "success"), ("organization_lifecycle_change", "failure"),
                               ("organization_lifecycle_change", "success")]],
        *[observed(metric, {"operation": operation, "outcome": "success"})
          for operation in ["configuration_read", "configuration_change", "configuration_history",
                            "configuration_activity", "configuration_replay", "operational_lifecycle_review",
                            "operational_lifecycle_directory", "operational_lifecycle_replay", "operational_lifecycle_change"]
          for metric in ["strataai_organization_requests", "strataai_organization_duration_count"]],
        *[observed(metric, {"operation": "configuration_change", "outcome": "conflict", "error_code": "version_conflict"})
          for metric in ["strataai_organization_requests", "strataai_organization_duration_count"]],
        *[observed(metric, {"operation": "configuration_read", "outcome": "denied"})
          for metric in ["strataai_organization_requests", "strataai_organization_duration_count"]],
        observed("strataai_activity_client_events", {"action": "organization_creation_disclosure", "kind": "open"}),
        observed("strataai_activity_client_events", {"action": "organization_creation", "kind": "retry"}),
        observed("strataai_activity_client_events", {"action": "organization_creation", "kind": "success"}),
        observed("strataai_activity_client_duration_count", {"action": "organization_creation", "kind": "success"}),
        observed("strataai_activity_client_events", {"action": "organization_settings_disclosure", "kind": "open"}),
        observed("strataai_activity_client_events", {"action": "organization_settings_read", "kind": "retry"}),
        observed("strataai_activity_client_events", {"action": "organization_settings_update", "kind": "use"}),
        observed("strataai_activity_client_events", {"action": "organization_settings_update", "kind": "success"}),
        observed("strataai_activity_client_duration_count", {"action": "organization_settings_update", "kind": "success"}),
        observed("strataai_organization_requests", {"operation": "create", "outcome": "success"}),
        observed("strataai_organization_duration_count", {"operation": "create", "outcome": "success"}),
        observed("strataai_organization_requests", {"operation": "read", "outcome": "denied"}),
        observed("strataai_organization_duration_count", {"operation": "read", "outcome": "denied"}),
        observed("strataai_checklist_client_events", {"action": "disclosure", "kind": "open"}),
        observed("strataai_checklist_client_events", {"action": "create", "kind": "use"}),
        observed("strataai_checklist_client_events", {"action": "create", "kind": "success"}),
        observed("strataai_checklist_client_duration_count", {"action": "create", "kind": "success"}),
        observed("strataai_board_sharing_requests", {"operation": "checklist_read", "outcome": "success"}),
        observed("strataai_board_sharing_duration_count", {"operation": "checklist_read", "outcome": "success"}),
        observed("strataai_activity_client_events", {"action": "card_disclosure", "kind": "open"}),
        observed("strataai_activity_client_events", {"action": "board_disclosure", "kind": "open"}),
        observed("strataai_activity_client_events", {"action": "card_read", "kind": "retry"}),
        observed("strataai_activity_client_events", {"action": "card_read", "kind": "success"}),
        observed("strataai_activity_client_duration_count", {"action": "card_read", "kind": "success"}),
        observed("strataai_board_sharing_requests", {"operation": "card_activity_read", "outcome": "success"}),
        observed("strataai_board_sharing_requests", {"operation": "board_activity_read", "outcome": "success"}),
        observed("strataai_activity_client_events", {"action": "comment_disclosure", "kind": "open"}),
        observed("strataai_activity_client_events", {"action": "comment_create", "kind": "use"}),
        observed("strataai_activity_client_events", {"action": "comment_create", "kind": "success"}),
        observed("strataai_activity_client_events", {"action": "mention_selection", "kind": "use"}),
        observed("strataai_activity_client_duration_count", {"action": "comment_create", "kind": "success"}),
        observed("strataai_board_sharing_requests", {"operation": "comment_create", "outcome": "success"}),
        observed("strataai_board_sharing_requests", {"operation": "comment_read", "outcome": "success"}),
    ])


if __name__ == "__main__":
    source, revision, version, target = sys.argv[1:]
    if not verify(Path(source).read_text(), revision, version):
        sys.exit(1)
    Path(target).write_text(json.dumps({"schemaVersion": 1, "revision": revision, "status": "passed",
        "topology": "exact API through Nginx to pinned OTLP Collector", "collectorVersion": "0.161.0",
        "verified": {"clientEvents": True, "clientDuration": True, "serverRequests": True,
                     "serverDuration": True, "activityClientEvents": True, "activityClientDuration": True,
                     "activityServerReads": True, "commentClientEvents": True, "commentClientDuration": True,
                     "commentServerOperations": True, "organizationServerOperations": True, "organizationClientEvents": True,
                     "configurationLifecycleServerOperations": True, "configurationConflictAndDenial": True,
                     "configurationClientEvents": True, "configurationClientDuration": True,
                     "httpRequestsAndDuration": True, "httpRejectionOutcomes": True,
                     "fixedBuildMetadata": True, "privateFieldsExcluded": True}}) + "\n")
