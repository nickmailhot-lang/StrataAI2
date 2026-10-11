"""Bounded, sequential PRD-27 HTTP measurements; retain no private sources."""
import argparse
import http.cookiejar
import json
import math
from pathlib import Path
import re
import sys
import time
import urllib.request
import uuid

SAMPLES = 25
WARMUP = 5
TARGET_MS = 500
OPERATIONS = ("configuration_change", "configuration_read", "operational_lifecycle_change", "operational_lifecycle_review")


def summarize(values):
    if len(values) != SAMPLES or any(not math.isfinite(value) or value < 0 for value in values):
        raise ValueError("invalid_samples")
    ordered = sorted(values)
    return {"samples": len(values), "minimumMs": round(ordered[0], 3), "maximumMs": round(ordered[-1], 3),
            "p50Ms": ordered[math.ceil(len(values) * .50) - 1],
            "p95Ms": ordered[math.ceil(len(values) * .95) - 1],
            "targetMet": ordered[math.ceil(len(values) * .95) - 1] < TARGET_MS}


def configuration(index, organization, board, listing):
    return {"legalName": f"Private latency legal name {index}", "jurisdiction": "CA-BC", "timezone": "America/Vancouver",
            "corporationIdentifier": organization, "civicAddress": "Private fixture address " + "x" * 900,
            "lotCount": 512, "fiscalYearEndMonth": 12, "fiscalYearEndDay": 31, "agmCycleMonths": 12,
            "depreciationReportCycleMonths": 36, "insuranceRenewalDate": "2027-12-31",
            "managementCompanyName": "Private fixture management", "intakeBoardId": board, "intakeListId": listing,
            "emergencyContacts": [{"name": f"Private contact {n}", "email": f"fixture-{n}@example.test", "phone": "+1 555 0100"} for n in range(32)],
            "defaultCategories": [f"Category {n} " + "x" * 100 for n in range(32)],
            "defaultPriorities": [f"Priority {n} " + "x" * 100 for n in range(32)],
            "jurisdictionPolicies": [{"key": f"fixture_policy_{n}", "value": "x" * 1200,
                                      "source": "Explicit fixture source " + "x" * 100,
                                      "notes": "Fixture values confer no legal compliance."} for n in range(32)]}


def measure(request, organization, actor, board, listing, revision, topology):
    if not re.fullmatch(r"[0-9a-f]{40,64}", revision) or topology not in ("exact-release-images", "local-compiled-runtime"):
        raise ValueError("invalid_identity")
    for identifier in (organization, actor, board, listing):
        uuid.UUID(identifier)
    measurements = {operation: [] for operation in OPERATIONS}
    def timed(operation, method, path, body=None, key=None, measured=False):
        started = time.perf_counter()
        status, result = request(method, path, body, key)
        elapsed = (time.perf_counter() - started) * 1000
        if status != 200:
            raise AssertionError("authorized_measurement_failed")
        if measured:
            measurements[operation].append(elapsed)
        return result
    path = f"/organizations/{organization}/configuration"
    version = timed("configuration_read", "GET", path)["version"]
    if version != 0:
        raise AssertionError("fresh_configuration_required")
    payload_sizes = []
    for index in range(WARMUP + SAMPLES):
        values = configuration(index, organization, board, listing)
        payload_sizes.append(len(json.dumps(values, separators=(",", ":")).encode()))
        changed = timed("configuration_change", "PATCH", path, {"version": version, "configuration": values}, str(uuid.uuid4()), index >= WARMUP)
        if changed["organizationId"] != organization or changed["actorId"] != actor or changed["version"] != version + 1 or changed["configuration"] != values:
            raise AssertionError("configuration_acknowledgment_invalid")
        version += 1
        current = timed("configuration_read", "GET", path, measured=index >= WARMUP)
        if current["organizationId"] != organization or current["version"] != version or current["revision"] != changed:
            raise AssertionError("configuration_refresh_invalid")
    path = f"/organizations/{organization}/operational-lifecycle"
    current = timed("operational_lifecycle_review", "GET", path)
    if current["status"] != "ACTIVE":
        raise AssertionError("active_parent_required")
    version = current["version"]
    for index in range(WARMUP + SAMPLES):
        target = "SUSPENDED" if index % 2 == 0 else "ACTIVE"
        changed = timed("operational_lifecycle_change", "PATCH", path, {"version": version, "status": target}, str(uuid.uuid4()), index >= WARMUP)
        if changed["organizationId"] != organization or changed["actorId"] != actor or changed["version"] != version + 1 or changed["status"] != target:
            raise AssertionError("lifecycle_acknowledgment_invalid")
        version += 1
        current = timed("operational_lifecycle_review", "GET", path, measured=index >= WARMUP)
        if current["organizationId"] != organization or current["actorId"] != actor or current["version"] != version or current["status"] != target:
            raise AssertionError("lifecycle_refresh_invalid")
    stats = {operation: summarize(values) for operation, values in measurements.items()}
    met = all(result["targetMet"] for result in stats.values())
    return {"schemaVersion": 1, "revision": revision, "topology": topology, "status": "passed" if met else "target_not_met",
            "targetP95Ms": TARGET_MS, "sampleMethod": "nearest-rank", "warmupPerOperation": WARMUP,
            "concurrency": 1, "setupExcluded": True, "externalProvidersExcluded": True,
            "fixture": {"configurationFields": 18, "collectionSize": 32, "maximumPayloadBytes": max(payload_sizes),
                        "configurationRevisions": WARMUP + SAMPLES, "lifecycleTransitions": WARMUP + SAMPLES},
            "verified": {"freshKeys": True, "owningIntake": True, "authoritativeRefresh": True, "actorBoundAcknowledgments": True},
            "operations": stats}


def main():
    parser = argparse.ArgumentParser()
    for argument in ("base", "cookies", "organization", "actor", "board", "listing", "revision", "output"):
        parser.add_argument("--" + argument, required=True)
    args = parser.parse_args()
    if args.base != "http://localhost:8088":
        raise ValueError("unexpected_release_edge")
    jar = http.cookiejar.MozillaCookieJar(args.cookies); jar.load(ignore_discard=True)
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *_):
            raise ValueError("unexpected_redirect")
    client = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(jar))
    def request(method, path, body=None, key=None):
        headers = {"X-StrataAI-Request": "1", "Content-Type": "application/json", "X-StrataAI-Expected-Actor": args.actor}
        # This fixture's cookie jar is restricted to its own configured edge.
        headers["Cookie"] = "; ".join(cookie.name + "=" + cookie.value for cookie in jar)
        if key: headers["Idempotency-Key"] = key
        payload = None if body is None else json.dumps(body).encode()
        with client.open(urllib.request.Request(args.base + path, data=payload, headers=headers, method=method), timeout=30) as response:
            return response.status, json.load(response)
    result = measure(request, args.organization, args.actor, args.board, args.listing, args.revision, "exact-release-images")
    output = Path(args.output); output.parent.mkdir(parents=True, exist_ok=True); output.write_text(json.dumps(result) + "\n")
    raise SystemExit(0 if result["status"] == "passed" else 1)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("Organization latency fixture failed; private HTTP material was not printed.", file=sys.stderr)
        raise SystemExit(1)
