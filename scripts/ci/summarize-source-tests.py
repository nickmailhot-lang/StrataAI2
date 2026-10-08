"""Retain result identities/outcomes, never raw assertions, output, or attachments."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import xml.etree.ElementTree as ET

OUTCOMES = ("passed", "failed", "skipped", "error")
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def build_identity(value):
    required = {"repository", "commitSha", "shortSha", "workflowRunId", "workflowRunNumber",
                "workflowRunAttempt", "version", "releaseVersion", "imageTag", "createdAt", "images"}
    if not isinstance(value, dict) or set(value) != required:
        raise ValueError("invalid_identity")
    patterns = {"repository": r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", "commitSha": r"(?:[a-f0-9]{40}|[a-f0-9]{64})",
                "workflowRunId": r"[1-9][0-9]*", "workflowRunNumber": r"[1-9][0-9]*", "workflowRunAttempt": r"[1-9][0-9]*",
                "version": r"[0-9A-Za-z.+-]{1,80}", "createdAt": r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z"}
    if any(not isinstance(value[key], str) or not re.fullmatch(pattern, value[key]) for key, pattern in patterns.items()):
        raise ValueError("invalid_identity")
    if value["shortSha"] != value["commitSha"][:12] or value["imageTag"] != value["commitSha"]:
        raise ValueError("invalid_identity")
    if value["releaseVersion"] not in (None, value["version"]):
        raise ValueError("invalid_identity")
    if value["images"] != {host: f"strataai-{host}:{value['commitSha']}" for host in ("web", "api", "worker")}:
        raise ValueError("invalid_identity")
    return value


def duration(value, trx=False):
    if trx:
        match = re.fullmatch(r"([0-9]+):([0-5][0-9]):([0-5][0-9](?:\.[0-9]+)?)", value)
        if not match:
            raise ValueError("invalid_duration")
        seconds = int(match[1]) * 3600 + int(match[2]) * 60 + float(match[3])
    else:
        seconds = float(value)
    if not math.isfinite(seconds) or not 0 <= seconds <= 86400:
        raise ValueError("invalid_duration")
    return round(seconds * 1000, 3)


def source_name(value, trx=False):
    if trx:
        return value if value.startswith("StrataAI.") and re.fullmatch(r"[A-Za-z0-9_.+]{1,512}", value) else "unclassified"
    normalized = value.replace("\\", "/")
    if "/src/" in normalized:
        normalized = "src/" + normalized.split("/src/", 1)[1]
    return normalized if re.fullmatch(r"(?:src|tests)/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.(?:test|spec)\.[cm]?[jt]sx?", normalized) else "unclassified"


def case(identity, source, outcome, elapsed):
    return {"caseId": hashlib.sha256(identity.encode("utf-8")).hexdigest(), "source": source,
            "outcome": outcome, "durationMs": elapsed}


def parse_report(path, kind):
    if path.stat().st_size > 50 * 1024 * 1024:
        raise ValueError("oversized_report")
    root = ET.parse(path).getroot()
    cases = []
    if kind == "junit":
        if root.tag not in ("testsuites", "testsuite"):
            raise ValueError("invalid_report")
        for index, row in enumerate(root.iter("testcase")):
            outcome = "error" if row.find("error") is not None else "failed" if row.find("failure") is not None else "skipped" if row.find("skipped") is not None else "passed"
            identity = f"{row.get('classname', '')}\0{row.get('name', '')}\0{index}"
            cases.append(case(identity, source_name(row.get("classname", "")), outcome, duration(row.get("time", "0"))))
        if root.get("tests") is not None and int(root.get("tests")) != len(cases):
            raise ValueError("incomplete_report")
        for attribute, outcome in (("failures", "failed"), ("errors", "error"), ("skipped", "skipped")):
            if root.get(attribute) is not None and int(root.get(attribute)) != sum(row["outcome"] == outcome for row in cases):
                raise ValueError("incomplete_report")
    else:
        if root.tag != "{" + TRX_NS["t"] + "}TestRun":
            raise ValueError("invalid_report")
        sources = {}
        for definition in root.findall("t:TestDefinitions/t:UnitTest", TRX_NS):
            method = definition.find("t:TestMethod", TRX_NS)
            if method is not None:
                sources[definition.get("id")] = source_name(method.get("className", "").split(",", 1)[0] + "." + method.get("name", ""), trx=True)
        mapping = {"Passed": "passed", "Failed": "failed", "NotExecuted": "skipped"}
        for index, row in enumerate(root.findall("t:Results/t:UnitTestResult", TRX_NS)):
            outcome = mapping.get(row.get("outcome"), "error")
            identity = f"{row.get('testId', '')}\0{row.get('testName', '')}\0{index}"
            cases.append(case(identity, sources.get(row.get("testId"), "unclassified"), outcome, duration(row.get("duration", "00:00:00"), trx=True)))
        counters = root.find("t:ResultSummary/t:Counters", TRX_NS)
        if counters is None or int(counters.get("total", "-1")) != len(cases):
            raise ValueError("incomplete_report")
        for attribute, outcome in (("passed", "passed"), ("failed", "failed"), ("notExecuted", "skipped")):
            if counters.get(attribute) is not None and int(counters.get(attribute)) != sum(row["outcome"] == outcome for row in cases):
                raise ValueError("incomplete_report")
    if not cases or all(row["outcome"] == "skipped" for row in cases):
        raise ValueError("empty_report")
    return cases


def summarize(paths, kind, metadata):
    reports = []
    refused = False
    for index, path in enumerate(paths):
        status, cases = "complete", []
        try:
            cases = parse_report(path, kind)
        except FileNotFoundError:
            status, refused = "missing", True
        except (ValueError, ET.ParseError, OSError, OverflowError):
            status, refused = "invalid", True
        counts = {outcome: sum(row["outcome"] == outcome for row in cases) for outcome in OUTCOMES}
        reports.append({"reportIndex": index, "status": status, "counts": counts, "cases": cases})
    return {"schemaVersion": 1, "runner": kind, "build": build_identity(metadata), "reports": reports}, refused


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--format", required=True, choices=("junit", "trx"))
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("reports", nargs="+", type=Path)
    args = parser.parse_args()
    try:
        metadata = build_identity(json.loads(args.metadata.read_text(encoding="utf-8")))
        for variable, field in (("GITHUB_SHA", "commitSha"), ("GITHUB_REPOSITORY", "repository"), ("GITHUB_RUN_ID", "workflowRunId")):
            if os.environ.get(variable) and os.environ[variable] != metadata[field]:
                raise ValueError("wrong_workflow_identity")
        summary, refused = summarize(args.reports, args.format, metadata)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(summary, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        failed = any(report["counts"]["failed"] or report["counts"]["error"] for report in summary["reports"])
        print("Source result summary retained; reports complete." if not refused else "Source result summary retained; required report missing or invalid.")
        return 1 if refused or failed else 0
    except (ValueError, OSError, TypeError, KeyError):
        print("Source result summary identity or publication failed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
