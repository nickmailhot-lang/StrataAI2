"""Verify private startup logs; publish only fixed metadata and aggregate counts."""
import datetime
import json
from pathlib import Path
import re
import sys
import uuid

FIELDS = {"Timestamp", "LogLevel", "Component", "BuildRevision", "BuildVersion", "Category",
          "EventId", "EventName", "CorrelationId", "OrganizationId", "Message", "ExceptionPresent"}
MAX_BYTES = 32 * 1024 * 1024


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate_field")
        result[key] = value
    return result


def verify(api, worker, revision, version):
    if not re.fullmatch(r"[a-f0-9]{40}|[a-f0-9]{64}", revision) or not re.fullmatch(r"[0-9A-Za-z.+_-]{1,80}", version):
        raise ValueError("invalid_build")
    counts = {}
    for component, raw in (("strataai-api", api), ("strataai-worker", worker)):
        if len(raw.encode("utf-8")) > MAX_BYTES:
            raise ValueError("oversize_log")
        rows = [line for line in raw.splitlines() if line.strip()]
        if not rows or len(rows) > 10000:
            raise ValueError("invalid_record_count")
        for line in rows:
            row = json.loads(line, object_pairs_hook=unique_object)
            if not isinstance(row, dict) or set(row) != FIELDS:
                raise ValueError("invalid_shape")
            if row["Component"] != component or row["BuildRevision"] != revision or row["BuildVersion"] != version:
                raise ValueError("wrong_build_or_service")
            if not isinstance(row["Timestamp"], str) or datetime.datetime.fromisoformat(row["Timestamp"]).utcoffset() != datetime.timedelta(0):
                raise ValueError("invalid_timestamp")
            if row["LogLevel"] not in {"Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"}:
                raise ValueError("invalid_severity")
            if not isinstance(row["Category"], str) or not row["Category"] or not isinstance(row["Message"], str):
                raise ValueError("invalid_text")
            if type(row["EventId"]) is not int or not -(2**31) <= row["EventId"] < 2**31:
                raise ValueError("invalid_event")
            if row["EventName"] is not None and not isinstance(row["EventName"], str):
                raise ValueError("invalid_event_name")
            correlation = row["CorrelationId"]
            if correlation is not None and (not isinstance(correlation, str) or not re.fullmatch(r"[A-Za-z0-9_.-]{1,64}", correlation)):
                raise ValueError("invalid_correlation")
            organization = row["OrganizationId"]
            if organization is not None and (not isinstance(organization, str)
                    or not re.fullmatch(r"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", organization)
                    or uuid.UUID(organization).int == 0):
                raise ValueError("invalid_organization")
            if type(row["ExceptionPresent"]) is not bool:
                raise ValueError("invalid_exception_flag")
        counts[component] = len(rows)
    return {"schemaVersion": 1, "revision": revision, "version": version, "status": "passed",
            "records": counts, "verified": {"recordShape": True, "serviceAndBuild": True,
            "utcTimestamps": True, "typedIdentifiers": True, "noRawStateScopeExceptionFields": True}}


def read_private(path):
    with Path(path).open("rb") as source:
        raw = source.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError("oversize_log")
    return raw.decode("utf-8")


if __name__ == "__main__":
    try:
        api, worker, revision, version, target = sys.argv[1:]
        evidence = verify(read_private(api), read_private(worker), revision, version)
        with Path(target).open("x", encoding="utf-8", newline="\n") as output:
            output.write(json.dumps(evidence) + "\n")
    except Exception:
        print("Console record verification failed.", file=sys.stderr)
        sys.exit(1)
