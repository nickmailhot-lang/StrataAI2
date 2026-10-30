"""Refuse omitted, duplicated, failed or malformed API batch results."""
import json
import sys
from pathlib import Path
import xml.etree.ElementTree as ET


def validate(inventory, directory):
    methods = inventory["methods"]
    batches = inventory["batches"]
    flattened = [method for batch in batches for method in batch]
    if not methods or len(set(methods)) != len(methods) or sorted(flattened) != sorted(methods):
        raise ValueError("Invalid API inventory or batch partition")
    expected_files = {f"batch-{i:03}.xml" for i in range(len(batches))}
    if {p.name for p in Path(directory).glob("*.xml")} != expected_files:
        raise ValueError("Missing or unexpected API result files")
    counts = {"passed": 0, "skipped": 0, "total": 0}
    for i, batch in enumerate(batches):
        root = ET.parse(Path(directory) / f"batch-{i:03}.xml").getroot()
        assemblies = root.findall("assembly")
        if len(assemblies) != 1:
            raise ValueError("Exactly one API assembly result is required")
        assembly = assemblies[0]
        tests = assembly.findall(".//test")
        if not tests or int(assembly.attrib["total"]) != len(tests):
            raise ValueError("Empty or inconsistent API result")
        if any(int(assembly.attrib[key]) != 0 for key in ("failed", "errors", "not-run")):
            raise ValueError("Failed, errored or unexecuted API cases")
        actual = {f"{t.attrib['type']}.{t.attrib['method']}" for t in tests}
        if actual != set(batch):
            raise ValueError("API batch omitted or duplicated selected methods")
        statuses = [t.attrib["result"] for t in tests]
        if any(status not in ("Pass", "Skip") for status in statuses):
            raise ValueError("Unpassed API case")
        for key, status in (("passed", "Pass"), ("skipped", "Skip")):
            count = statuses.count(status)
            if count != int(assembly.attrib[key]):
                raise ValueError("Inconsistent API status counts")
            counts[key] += count
        counts["total"] += len(tests)
    return {"result": "Passed", "methods": len(methods), "batches": len(batches), **counts,
            "qualification": "All discovered API methods covered; existing skips remain separate. Hosted single-process qualification remains required."}


if __name__ == "__main__":
    inventory_path, result_directory, output_path = sys.argv[1:]
    summary = validate(json.loads(Path(inventory_path).read_text()), result_directory)
    Path(output_path).write_text(json.dumps(summary, indent=2) + "\n")
