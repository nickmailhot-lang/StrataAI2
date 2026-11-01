"""Validate complete selected API methods, without retaining private theory data."""
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def validate(selection, report):
    methods = selection.get("methods", [])
    if (selection.get("theoryRows") != "all" or not methods
            or len(set(methods)) != len(methods)):
        raise ValueError("Explicit complete API method selection required")
    text = Path(report).read_text()
    if "<!DOCTYPE" in text.upper() or "<!ENTITY" in text.upper():
        raise ValueError("Unexpected XML declarations")
    root = ET.fromstring(text)
    if root.tag != "{" + NS["t"] + "}TestRun":
        raise ValueError("Expected TRX test run")
    definitions = {}
    for definition in root.findall("t:TestDefinitions/t:UnitTest", NS):
        identity = definition.get("id")
        method = definition.find("t:TestMethod", NS)
        if not identity or identity in definitions or method is None:
            raise ValueError("Missing or duplicate API test definition")
        class_name = method.get("className", "").split(",", 1)[0]
        definitions[identity] = class_name + "." + method.get("name", "")
    rows = root.findall("t:Results/t:UnitTestResult", NS)
    executions = set()
    covered = set()
    for row in rows:
        execution = row.get("executionId")
        method = definitions.get(row.get("testId"))
        if not execution or execution in executions or method not in methods:
            raise ValueError("Missing, duplicate or unselected API execution")
        if row.get("outcome") != "Passed":
            raise ValueError("Selected API case failed, skipped or did not execute")
        executions.add(execution)
        covered.add(method)
    if covered != set(methods):
        raise ValueError("API report omitted selected methods")
    summaries = root.findall("t:ResultSummary", NS)
    if len(summaries) != 1 or summaries[0].get("outcome") != "Completed":
        raise ValueError("API run did not complete")
    counters = summaries[0].find("t:Counters", NS)
    if counters is None:
        raise ValueError("Missing API counters")
    for field in ("total", "executed", "passed"):
        if int(counters.get(field, "-1")) != len(rows):
            raise ValueError("API counters do not match executed cases")
    for field, value in counters.attrib.items():
        if field not in ("total", "executed", "passed") and int(value) != 0:
            raise ValueError("Nonpassing API counters")
    return {"result": "Passed", "methods": len(methods), "cases": len(rows),
            "theoryRows": "all", "productionQualified": False,
            "scope": "Selected existing methods; full API regression deferred"}


if __name__ == "__main__":
    selection, report, output = sys.argv[1:]
    result = validate(json.loads(Path(selection).read_text()), report)
    Path(output).write_text(json.dumps(result, indent=2) + "\n")
