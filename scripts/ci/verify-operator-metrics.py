"""Validate real Collector output; retain only fixed verification metadata."""
import json
import math
import re
import sys
from pathlib import Path


_STRING = r'"(?:\\[\\"n]|[^"\\\r\n])*"'
_LABEL = re.compile(r'[ \t]*([a-zA-Z_][a-zA-Z0-9_]*|' + _STRING + r')[ \t]*=[ \t]*(' + _STRING + r')[ \t]*')


def parse_labels(raw):
    """Consume every label; never discard malformed text or overwrite a key."""
    labels = {}
    position = 0
    while raw[position:].strip():
        match = _LABEL.match(raw, position)
        if not match:
            return None
        name, value = match.groups()
        if name.startswith('"'):
            name = name[1:-1]
        if name in labels:
            return None
        labels[name] = value[1:-1]
        position = match.end()
        if position == len(raw):
            break
        if raw[position] != ',':
            return None
        position += 1  # One final comma is valid in Prometheus text format.
    return labels


def verify(raw, revision, version, *, backlog_available=1):
    if type(backlog_available) is not int or backlog_available not in {0, 1}:
        return False
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
    names.add("strataai_worker_jobs")
    names.add("strataai_worker_loop_failures")
    names.update({"strataai_worker_scoped_backlog", "strataai_worker_scoped_backlog_available"})
    names.update({"strataai_worker_identity_backlog", "strataai_worker_identity_backlog_available"})
    names.update("strataai_http_server_duration" + suffix for suffix in ["_bucket", "_count", "_sum"])
    for line in raw.splitlines():
        if not line.startswith("strataai_"):
            continue
        match = re.fullmatch(r'(\w+)\{([^}]*)\} ([0-9.eE+\-]+)', line)
        if not match or match[1] not in names:
            return False
        labels = parse_labels(match[2])
        if labels is None:
            return False
        permitted = allowed | {"method", "status_code"} if match[1].startswith("strataai_http_server_") else allowed
        worker = match[1] in {"strataai_worker_jobs", "strataai_worker_loop_failures",
                              "strataai_worker_scoped_backlog", "strataai_worker_scoped_backlog_available",
                              "strataai_worker_identity_backlog", "strataai_worker_identity_backlog_available"}
        if worker:
            permitted = {"queue", "job", "instance", "service_name", "service_namespace", "service_version", "strataai_build_revision"}
            if match[1] == "strataai_worker_jobs":
                permitted.add("outcome")
            if match[1] in {"strataai_worker_scoped_backlog", "strataai_worker_identity_backlog"}:
                permitted.add("state")
        if set(labels) - permitted or labels.get("instance", "") != "":
            return False
        if labels.get("strataai_build_revision") != revision or labels.get("service_version") != version:
            return False
        service = "strataai-worker" if worker else "strataai-api"
        if labels.get("service_name") != service or labels.get("service_namespace") != "strataai":
            return False
        # The pinned Collector derives job from these resource attributes; it
        # cannot be a free-form host, recipient, tenant or alternate service.
        if "job" in labels and labels["job"] != f"strataai/{service}":
            return False
        if worker:
            outcomes = {"organization": {"completed", "retried", "lease_lost", "unknown"},
                        "identity_email": {"sent", "cancelled", "retried", "failed", "lease_lost", "unknown"}}
            if labels.get("queue") not in outcomes:
                return False
            if match[1] == "strataai_worker_jobs" and labels.get("outcome") not in outcomes[labels["queue"]]:
                return False
            if not math.isfinite(float(match[3])) or float(match[3]) < 0:
                return False
            if match[1].startswith(("strataai_worker_scoped_backlog", "strataai_worker_identity_backlog")):
                expected_queue = "identity_email" if match[1].startswith("strataai_worker_identity_") else "organization"
                if labels.get("queue") != expected_queue or not float(match[3]).is_integer():
                    return False
                if match[1] in {"strataai_worker_scoped_backlog", "strataai_worker_identity_backlog"} and labels.get("state") not in {"pending", "running"}:
                    return False
                if match[1].endswith("_available") and float(match[3]) not in {0, 1}:
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

    # Identity mail is disabled in some fixtures. When present, it must report
    # one complete valid observation, never an unexplained partial snapshot.
    identity = [(name, labels, value) for name, labels, value in samples if name.startswith("strataai_worker_identity_backlog")]
    if identity:
        availability = [value for name, _, value in identity if name.endswith("_available")]
        if len(availability) != 1:
            return False
        for state in ["pending", "running"]:
            counts = [value for name, labels, value in identity if name == "strataai_worker_identity_backlog" and labels.get("state") == state]
            if len(counts) > 1 or (availability[0] == 1 and len(counts) != 1):
                return False

    def observed(name, expected):
        return any(metric == name and (value == 1 if name.startswith(("strataai_checklist_client_", "strataai_activity_client_")) else value >= 1)
                   and all(labels.get(key) == val for key, val in expected.items())
                   for metric, labels, value in samples)

    return all([
        not backlog_available or observed("strataai_worker_scoped_backlog", {"queue": "organization", "state": "pending"}),
        not backlog_available or observed("strataai_worker_scoped_backlog", {"queue": "organization", "state": "running"}),
        [value for name, labels, value in samples if name == "strataai_worker_scoped_backlog_available"] == [backlog_available],
        observed("strataai_worker_jobs", {"queue": "organization", "outcome": "completed"}),
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
    arguments = sys.argv[1:]
    if len(arguments) not in {4, 5} or (len(arguments) == 5 and arguments[-1] != "--backlog-unavailable"):
        sys.exit(2)
    source, revision, version, target = arguments[:4]
    expected_available = 0 if len(arguments) == 5 else 1
    if not verify(Path(source).read_text(), revision, version, backlog_available=expected_available):
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
                     "workerCommittedJobOutcome": True, "workerBuildIdentity": True,
                     "workerScopedBacklog": bool(expected_available), "workerBacklogAvailable": bool(expected_available),
                     "workerBacklogObservationAvailability": expected_available,
                     "fixedBuildMetadata": True, "privateFieldsExcluded": True}}) + "\n")
