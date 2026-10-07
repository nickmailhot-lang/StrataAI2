"""Validate real Collector output; retain only fixed verification metadata."""
import json
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
    for line in raw.splitlines():
        if not line.startswith("strataai_"):
            continue
        match = re.fullmatch(r'(\w+)\{([^}]*)\} ([0-9.eE+\-]+)', line)
        if not match or match[1] not in names:
            return False
        labels = dict(re.findall(r'(\w+)="((?:\\.|[^"\\])*)"', match[2]))
        if set(labels) - allowed or labels.get("instance", "") != "":
            return False
        if labels.get("strataai_build_revision") != revision or labels.get("service_version") != version:
            return False
        if labels.get("service_name") != "strataai-api" or labels.get("service_namespace") != "strataai":
            return False
        samples.append((match[1], labels, float(match[3])))

    def observed(name, expected):
        return any(metric == name and (value == 1 if name.startswith(("strataai_checklist_client_", "strataai_activity_client_")) else value >= 1)
                   and all(labels.get(key) == val for key, val in expected.items())
                   for metric, labels, value in samples)

    return all([
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
                     "commentServerOperations": True, "organizationServerOperations": True, "organizationClientEvents": True, "fixedBuildMetadata": True, "privateFieldsExcluded": True}}) + "\n")
