"""Bounded live alert transport contract; no application or human recipient.

Run the unchanged release rules with pinned tools in retained runtime images.
Only an owned internal network, synthetic scrape target and local receiver exist.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tarfile
import tempfile
import time
import urllib.parse
import urllib.request
import uuid

ALERT = "StrataAiCollectorUnavailable"
LABELS = {"alertname": ALERT, "component": "metrics", "severity": "warning"}
ANNOTATIONS = {"summary": "StrataAI metrics scrape coverage is unavailable"}
RULES = {ALERT, "StrataAiApiErrors", "StrataAiApiLatency", "StrataAiApiHistogramUnavailable",
         "StrataAiWorkerLoopFailures", "StrataAiEmailDeliveryFailures",
         "StrataAiOrganizationBacklogUnknown", "StrataAiIdentityBacklogUnknown"}
TOOLS = [
    ("prometheus", "3.15.0", 111651578,
     "2a542df32eac02ee17b9d844fb2aa1de00dafa5476579ba8a3ba862e9d572ea0"),
    ("alertmanager", "0.34.1", 39222281,
     "265b9d1e55ef0d5306a436018af6d2b686c2ce051f03d968f7464ecb1372a7e8"),
]


def validate_notification(value):
    """Accept only the fixed fixture warning; never return or retain the body."""
    if not isinstance(value, dict) or set(value) != {
            "receiver", "status", "alerts", "groupLabels", "commonLabels", "commonAnnotations",
            "externalURL", "version", "groupKey", "truncatedAlerts", "routeLabels", "notification_reason"}:
        raise ValueError("notification_shape")
    if value["receiver"] != "fixture-local" or value["version"] != "4" or value["truncatedAlerts"] != 0:
        raise ValueError("notification_receiver")
    status = value["status"]
    if status not in {"firing", "resolved"} or value["commonLabels"] != LABELS or value["commonAnnotations"] != ANNOTATIONS:
        raise ValueError("notification_private_or_unknown_metadata")
    if value["routeLabels"] != {} or value["notification_reason"] != {
            "firing": "first notification", "resolved": "all alerts resolved"}[status]:
        raise ValueError("notification_route_or_reason")
    if value["groupLabels"] != {"alertname": ALERT, "component": "metrics"}:
        raise ValueError("notification_group")
    if value["externalURL"].rstrip("/") != "http://alertmanager:9093":
        raise ValueError("notification_external_url")
    if not isinstance(value["alerts"], list) or len(value["alerts"]) != 1:
        raise ValueError("notification_count")
    alert = value["alerts"][0]
    if set(alert) != {"status", "labels", "annotations", "startsAt", "endsAt", "generatorURL", "fingerprint"}:
        raise ValueError("notification_alert_shape")
    if alert["status"] != status or alert["labels"] != LABELS or alert["annotations"] != ANNOTATIONS:
        raise ValueError("notification_alert_metadata")
    url = urllib.parse.urlsplit(alert["generatorURL"])
    if url.scheme != "http" or url.hostname != "prometheus" or url.port != 9090 or url.username or url.password:
        raise ValueError("notification_generator")
    if not isinstance(alert["fingerprint"], str) or not re.fullmatch("[a-f0-9]{16}", alert["fingerprint"]):
        raise ValueError("notification_fingerprint")
    for field in ["startsAt", "endsAt"]:
        if datetime.fromisoformat(alert[field].replace("Z", "+00:00")).utcoffset() is None:
            raise ValueError("notification_clock")
    if any(marker in json.dumps(value) for marker in ["private-fixture-instance", "private_marker", "private@example.test"]):
        raise ValueError("notification_private_value")
    return status, alert["fingerprint"]


class HttpsRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, target):
        if urllib.parse.urlsplit(target).scheme != "https":
            raise ValueError("tool_redirect")
        return super().redirect_request(request, response, code, message, headers, target)


def download_tools(folder):
    opener = urllib.request.build_opener(HttpsRedirect())
    for name, version, size, digest in TOOLS:
        archive = folder / (name + ".tar.gz")
        url = f"https://github.com/prometheus/{name}/releases/download/v{version}/{name}-{version}.linux-amd64.tar.gz"
        received = 0
        with opener.open(url, timeout=60) as response, archive.open("xb") as output:
            while chunk := response.read(1024 * 1024):
                received += len(chunk)
                if received > size:
                    raise ValueError("tool_size")
                output.write(chunk)
        with archive.open("rb") as source:
            if received != size or hashlib.file_digest(source, "sha256").hexdigest() != digest:
                raise ValueError("tool_integrity")
        with tarfile.open(archive) as contents:
            member = contents.getmember(f"{name}-{version}.linux-amd64/{name}")
            if not member.isfile() or member.size > 256 * 1024 * 1024:
                raise ValueError("tool_member")
            with contents.extractfile(member) as source, (folder / name).open("xb") as output:
                shutil.copyfileobj(source, output)
        (folder / name).chmod(0o700)


def run(revision, output):
    if not re.fullmatch(r"[a-f0-9]{40}|[a-f0-9]{64}", revision) or output.exists():
        raise ValueError("fixture_arguments")
    repo = Path(__file__).resolve().parents[2]
    original_rules = repo / "deploy/metrics/alerts.yml"
    rules_hash = hashlib.sha256(original_rules.read_bytes()).hexdigest()
    docker = shutil.which("docker")
    if not docker:
        raise ValueError("docker_unavailable")

    def call(arguments, timeout=30):
        result = subprocess.run([docker, *arguments], capture_output=True, timeout=timeout)
        if result.returncode:
            raise RuntimeError("fixture_docker_operation")
        return result.stdout.decode().strip()

    images = {host: f"strataai-{host}:{revision}" for host in ["api", "web"]}
    identities = {}
    for host, image in images.items():
        item = json.loads(call(["image", "inspect", image]))[0]
        if item["Config"]["Labels"].get("org.opencontainers.image.revision") != revision:
            raise ValueError("fixture_image_revision")
        identities[host] = item["Id"]
    token = uuid.uuid4().hex
    owner = "com.strataai.alert-fixture=" + token
    temp_root = Path(tempfile.gettempdir()).resolve()
    folder = Path(tempfile.mkdtemp(prefix="strataai-alert-delivery-")).resolve()
    if folder.parent != temp_root:
        raise ValueError("fixture_directory")
    created = []
    network = None
    phase = "tools"
    record = {"schemaVersion": 1, "revision": revision, "status": "passed", "rulesSha256": rules_hash,
              "topology": "pinned Prometheus to pinned Alertmanager to isolated fixture receiver",
              "prometheusVersion": "3.15.0", "alertmanagerVersion": "0.34.1"}
    started = time.monotonic()
    try:
        download_tools(folder)
        (folder / "alerts.yml").write_bytes(original_rules.read_bytes())
        data = folder / "data"; data.mkdir()
        logs = folder / "logs"; logs.mkdir()
        (data / "metrics.txt").write_text(
            'strataai_http_server_duration_bucket{service_name="strataai-api",service_namespace="strataai",le="0.75"} 1\n'
            'strataai_http_server_duration_count{service_name="strataai-api",service_namespace="strataai"} 1\n', newline="\n")
        uid = str(os.getuid()) if hasattr(os, "getuid") else "0"
        user = "user root;\n" if uid == "0" else ""
        (folder / "nginx.conf").write_text(user + '''pid /tmp/nginx.pid;
master_process off;
error_log /dev/stderr warn;
events { worker_connections 64; }
http {
 open_file_cache off;
 client_body_temp_path /tmp/client-body;
 proxy_temp_path /tmp/proxy;
 fastcgi_temp_path /tmp/fastcgi;
 uwsgi_temp_path /tmp/uwsgi;
 scgi_temp_path /tmp/scgi;
 log_format delivery escape=default '$request_body';
 access_log off;
 server { listen 8080;
  location = /metrics { if (-f /tmp/unavailable) { return 503; } default_type text/plain; alias /data/metrics.txt; }
 }
 server { listen 8081; client_max_body_size 32k; client_body_buffer_size 32k;
  location = /webhook { access_log /logs/webhook.log delivery; proxy_pass http://127.0.0.1:8082; }
 }
 server { listen 8082; location / { return 200; } }
}
''')
        (folder / "prometheus.yml").write_text('''global:
 scrape_interval: 15s
 evaluation_interval: 1m
rule_files: [/fixture/alerts.yml]
scrape_configs:
 - job_name: strataai-collector
   honor_labels: true
   static_configs:
    - targets: [probe:8080]
      labels: {instance: private-fixture-instance}
alerting:
 alertmanagers:
  - static_configs:
     - targets: [alertmanager:9093]
''')
        (folder / "alertmanager.yml").write_text('''route:
 receiver: fixture-local
 group_by: [alertname, component]
 group_wait: 1s
 group_interval: 1s
 repeat_interval: 1h
receivers:
 - name: fixture-local
   webhook_configs:
    - url: http://probe:8081/webhook
      send_resolved: true
''')
        phase = "startup"
        network = call(["network", "create", "--internal", "--label", owner, "strataai-alert-" + token[:16]])

        def start(alias, host, executable, arguments):
            args = ["run", "--detach", "--pull", "never", "--no-healthcheck", "--read-only",
                    "--name", "strataai-alert-" + token[:16] + "-" + alias, "--label", owner,
                    "--network", network, "--network-alias", alias, "--user", uid,
                    "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true", "--pids-limit", "128",
                    "--memory", "512m", "--tmpfs", "/tmp:rw,noexec,nosuid,size=32m,mode=1777",
                    "--tmpfs", "/state:rw,noexec,nosuid,size=256m,mode=1777",
                    "--mount", f"type=bind,source={folder},target=/fixture,readonly",
                    "--mount", f"type=bind,source={data},target=/data,readonly",
                    "--mount", f"type=bind,source={logs},target=/logs",
                    "--entrypoint", executable, images[host], *arguments]
            identity = call(args)
            created.append(identity)
            if call(["inspect", "--format", "{{.Image}}", identity]) != identities[host]:
                raise ValueError("fixture_image_changed")
            return identity

        probe = start("probe", "web", "nginx", ["-c", "/fixture/nginx.conf", "-g", "daemon off;"])
        probe_started = call(["inspect", "--format", "{{.State.StartedAt}}", probe])
        start("alertmanager", "api", "/fixture/alertmanager", ["--config.file=/fixture/alertmanager.yml",
              "--storage.path=/state", "--cluster.listen-address=", "--web.listen-address=0.0.0.0:9093",
              "--web.external-url=http://alertmanager:9093"])
        prom = start("prometheus", "api", "/fixture/prometheus", ["--config.file=/fixture/prometheus.yml",
              "--storage.tsdb.path=/state", "--storage.tsdb.wal-segment-size=10MB",
              "--storage.tsdb.retention.time=1h", "--web.listen-address=0.0.0.0:9090",
              "--web.external-url=http://prometheus:9090"])

        def get(path):
            return json.loads(call(["exec", prom, "curl", "--fail", "--silent", "--max-time", "3",
                                   "http://127.0.0.1:9090" + path]))

        def wait(operation, seconds, message):
            deadline = time.monotonic() + seconds
            while time.monotonic() < deadline:
                for identity in created:
                    if call(["inspect", "--format", "{{.State.Running}}", identity]) != "true":
                        raise RuntimeError("fixture_process_exit")
                result = operation()
                if result:
                    return result
                time.sleep(2)
            raise RuntimeError(message)

        def ready():
            try:
                result = get("/api/v1/rules")
                return result if result.get("data", {}).get("groups") else None
            except RuntimeError:
                return None

        loaded = wait(ready, 30, "fixture_startup_timeout")
        groups = loaded["data"]["groups"]
        if len(groups) != 1 or groups[0]["interval"] != 60 or {r["name"] for r in groups[0]["rules"]} != RULES:
            raise ValueError("fixture_rules_changed")
        rule = next(r for r in groups[0]["rules"] if r["name"] == ALERT)
        if rule["duration"] != 120:
            raise ValueError("fixture_hold_changed")

        def notifications():
            path = logs / "webhook.log"
            if not path.exists():
                return []
            raw = path.read_bytes()
            if len(raw) > 128 * 1024:
                raise ValueError("fixture_receiver_limit")
            result = []
            for line in raw.splitlines(keepends=True):
                if line.endswith(b"\n"):
                    body = re.sub(rb"\\x([0-9A-Fa-f]{2})", lambda m: bytes([int(m[1], 16)]), line)
                    value = json.loads(body)
                    if value != {"probe": True}:
                        result.append(validate_notification(value))
            return result

        call(["exec", prom, "curl", "--fail", "--silent", "--max-time", "3", "-H", "Content-Type: application/json",
              "--data", '{"probe":true}', "http://probe:8081/webhook"])
        wait(lambda: (logs / "webhook.log").exists() and (logs / "webhook.log").stat().st_size,
             5, "fixture_receiver_probe")
        last_frame = None
        def state(expected):
            nonlocal last_frame
            rules = get("/api/v1/rules")["data"]["groups"][0]["rules"]
            if any(r["health"] not in {"ok", "unknown"} for r in rules):
                raise ValueError("fixture_evaluation_error")
            if any(r["health"] == "unknown" for r in rules):
                return None
            current = next(r for r in rules if r["name"] == ALERT)
            frame = (current["state"], current["health"], current["lastEvaluation"])
            if frame != last_frame:
                print("Warning evaluation: " + json.dumps(frame), flush=True)
                last_frame = frame
            return current if current["state"] == expected else None

        phase = "healthy_admission"
        query = "/api/v1/query?query=" + urllib.parse.quote('up{job="strataai-collector"}', safe="")

        def target_value(expected):
            samples = get(query)["data"]["result"]
            return len(samples) == 1 and samples[0]["value"][1] == expected

        wait(lambda: target_value("1") and state("inactive"), 90, "fixture_healthy_admission_timeout")
        if notifications():
            raise ValueError("fixture_unexpected_healthy_notification")
        phase = "outage_admission"
        call(["exec", probe, "touch", "/tmp/unavailable"])
        wait(lambda: target_value("0"), 45, "fixture_outage_admission_timeout")
        phase = "pending"
        print("Healthy scrape admitted; waiting for the unchanged two-minute warning hold.", flush=True)
        # Startup can evaluate an earlier healthy timestamp for another cycle.
        # Allow admission/recovery across two slots without changing the rules.
        wait(lambda: state("pending"), 150, "fixture_pending_timeout")
        if notifications():
            raise ValueError("fixture_premature_notification")
        phase = "firing"
        firing = wait(lambda: state("firing"), 190, "fixture_firing_timeout")
        active_at = datetime.fromisoformat(firing["alerts"][0]["activeAt"].replace("Z", "+00:00"))
        evaluated_at = datetime.fromisoformat(firing["lastEvaluation"].replace("Z", "+00:00"))
        if (evaluated_at - active_at).total_seconds() < 120:
            raise ValueError("fixture_premature_firing")
        events = wait(lambda: notifications(), 30, "fixture_firing_delivery_timeout")
        if events != [("firing", events[0][1])]:
            raise ValueError("fixture_firing_delivery_shape")
        fingerprint = events[0][1]
        phase = "resolved"
        call(["exec", probe, "rm", "/tmp/unavailable"])
        print("Firing warning delivered privately; recovering the same scrape target.", flush=True)
        wait(lambda: target_value("1") and state("inactive"), 160, "fixture_recovery_timeout")
        wait(lambda: ("resolved", fingerprint) in notifications(), 30, "fixture_resolved_delivery_timeout")
        if any(identity != fingerprint for _, identity in notifications()):
            raise ValueError("fixture_warning_identity_changed")
        if call(["inspect", "--format", "{{.State.StartedAt}}", probe]) != probe_started:
            raise ValueError("fixture_target_process_changed")
        if hashlib.sha256(original_rules.read_bytes()).hexdigest() != rules_hash or hashlib.sha256((folder / "alerts.yml").read_bytes()).hexdigest() != rules_hash:
            raise ValueError("fixture_source_changed")
        record["verified"] = {"originalRuleSet": True, "healthyBeforeOutage": True, "unchangedTwoMinuteHold": True,
                              "firingNotification": True, "sameWarningResolved": True, "sameTargetProcess": True,
                              "scrapeAvailabilityTransition": "1-0-1",
                              "fixedPrivateSafeLabels": True, "noExternalRecipient": True,
                              "noApplicationLoad": True}
        record["durationSeconds"] = round(time.monotonic() - started, 3)
    except Exception:
        print("Operator alert delivery failed at fixture phase: " + phase, flush=True)
        if phase in {"healthy_admission", "outage_admission", "pending", "firing", "resolved"}:
            for target in get("/api/v1/targets")["data"]["activeTargets"]:
                print("Controlled scrape diagnostic: " + target["health"] + ": " + target["lastError"], flush=True)
        for identity in created:
            diagnostic = subprocess.run([docker, "logs", "--tail", "12", identity], capture_output=True, timeout=10)
            print((diagnostic.stdout + diagnostic.stderr).decode(errors="replace"), flush=True)
        raise
    finally:
        # A failed docker-run can create a labelled container before returning
        # an error. Discover only this invocation's label, including that case.
        owned = call(["ps", "-aq", "--filter", "label=" + owner]).splitlines()
        for identity in reversed(owned):
            if call(["inspect", "--format", '{{index .Config.Labels "com.strataai.alert-fixture"}}', identity]) != token:
                raise ValueError("fixture_cleanup_identity")
            call(["rm", "--force", "--volumes", identity])
        for identity in call(["network", "ls", "-q", "--filter", "label=" + owner]).splitlines():
            call(["network", "rm", identity])
        if call(["ps", "-aq", "--filter", "label=" + owner]) or call(["network", "ls", "-q", "--filter", "label=" + owner]):
            raise ValueError("fixture_cleanup_leftovers")
        if folder.parent != temp_root or not folder.name.startswith("strataai-alert-delivery-") or folder.is_symlink():
            raise ValueError("fixture_cleanup_path")
        record["removedTemporaryBytes"] = sum(path.stat().st_size for path in folder.rglob("*") if path.is_file())
        shutil.rmtree(folder)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x") as target:
        json.dump(record, target)
        target.write("\n")
    print("Original warning firing/resolution and owned cleanup verified.", flush=True)


if __name__ == "__main__":
    def stop(number, _):
        raise SystemExit(128 + number)
    signal.signal(signal.SIGTERM, stop)
    parser = argparse.ArgumentParser()
    parser.add_argument("--revision", required=True)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    run(options.revision, options.output)
