"""Refuse incomplete selected web reports; retain existing capacity deferrals."""
import hashlib
from collections import Counter
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def validate(selection, report, deferred, discovery):
    files = selection.get('files', [])
    if (not files or len(set(files)) != len(files)
            or any(not re.fullmatch(r'apps/web/src/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.test\.tsx?', p) or '..' in p.split('/') for p in files)):
        raise ValueError('Explicit unique web test inventory required')
    allowed = {(row['source'], row['caseId']) for row in deferred['cases']}
    capacity = 'apps/web/src/features/kanban/BoardWindow.capacity.test.tsx'
    functional = 'apps/web/src/features/kanban/BoardWindow.test.tsx'
    if len(allowed) != 24 or any(source != capacity for source, _ in allowed):
        raise ValueError('Existing fixed capacity deferrals require review')
    expected=Counter()
    if not isinstance(discovery,list) or not discovery or not selection.get('root'):
        raise ValueError('Independent runtime web discovery required')
    for row in discovery:
        source=Path(row['file']).resolve().relative_to(Path(selection['root']).resolve()).as_posix()
        if source not in files or not row.get('name'):
            raise ValueError('Unexpected discovered web source/case')
        # JUnit places display names in XML attributes: literal CR/LF/tab use
        # XML's whitespace normalization, while JSON discovery preserves them.
        name=row['name'].replace('\r\n','\n').replace('\r','\n').replace('\n',' ').replace('\t',' ')
        expected[(source,hashlib.sha256((source+'\0'+name).encode()).hexdigest())]+=1
    missing=set(files)-{source for source,_ in expected}
    if missing and missing!={capacity}:
        raise ValueError('Discovery omitted selected web files')
    # Vitest runtime listing intentionally excludes skipped suites. Preserve the
    # original reviewed capacity inventory as deferred, never as executed proof.
    uncollected=allowed if capacity in missing else set()
    if capacity in files and hashlib.sha256((Path(selection['root'])/capacity).read_bytes()).hexdigest()!=deferred.get('sourceSha256'):
        raise ValueError('Deferred capacity source changed; review its original assertions')
    expected.update(uncollected)
    text = Path(report).read_text()
    if '<!DOCTYPE' in text.upper() or '<!ENTITY' in text.upper():
        raise ValueError('Unexpected XML declarations')
    root = ET.fromstring(text)
    if root.tag != 'testsuites':
        raise ValueError('Expected web JUnit report')
    seen_files, seen_cases = set(), Counter()
    passed = skipped = total = 0
    for suite in root.findall('testsuite'):
        source = 'apps/web/' + suite.get('name', '')
        if source not in files or source in seen_files:
            raise ValueError('Unexpected or duplicated web test file')
        seen_files.add(source)
        cases = suite.findall('testcase')
        if not cases or int(suite.get('tests', '-1')) != len(cases):
            raise ValueError('Empty or inconsistent selected web suite')
        if any(int(suite.get(k, '-1')) != 0 for k in ('errors', 'failures')):
            raise ValueError('Web suite failed or errored')
        local_skips, local_ids = 0, set()
        for case in cases:
            if case.get('classname') != suite.get('name') or not case.get('name'):
                raise ValueError('Missing or inconsistent web case identity')
            identity = (source, hashlib.sha256((source + '\0' + case.get('name')).encode()).hexdigest())
            seen_cases[identity]+=1
            if seen_cases[identity]>expected[identity]:
                raise ValueError('Unexpected or duplicated web case beyond source inventory')
            local_ids.add(identity)
            if case.find('failure') is not None or case.find('error') is not None:
                raise ValueError('Selected web case failed or errored')
            if identity in uncollected and case.find('skipped') is None:
                raise ValueError('Uncollected deferred case cannot count as execution')
            if case.find('skipped') is not None:
                if identity not in allowed:
                    raise ValueError('Unreviewed skipped web case')
                local_skips += 1
        if int(suite.get('skipped', '-1')) != local_skips:
            raise ValueError('Inconsistent web skip counters')
        if source == capacity and local_ids != allowed:
            raise ValueError('Original capacity case inventory changed')
        if source == functional and (len(cases) != 24 or local_skips):
            raise ValueError('Original 24 functional counterparts required')
        total += len(cases); skipped += local_skips; passed += len(cases) - local_skips
    if seen_files != set(files) or seen_cases!=expected or not passed:
        raise ValueError('Missing selected web files or no passing cases')
    if int(root.get('tests', '-1')) != total or any(int(root.get(k, '-1')) != 0 for k in ('errors', 'failures')):
        raise ValueError('Inconsistent aggregate web counters')
    return {'result': 'Passed', 'files': len(files), 'passed': passed,
            'existingDeferred': skipped, 'productionQualified': False}


if __name__ == '__main__':
    selection, report, deferred, discovery, output = sys.argv[1:]
    result = validate(json.loads(Path(selection).read_text()), report, json.loads(Path(deferred).read_text()),json.loads(Path(discovery).read_text()))
    Path(output).write_text(json.dumps(result, indent=2) + '\n')
