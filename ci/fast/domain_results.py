"""Retain complete Domain outcomes and exactly the three original load deferrals."""
import json
import hashlib
import re
import sys
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
RANK='StrataAI.Domain.Tests.RankTokenTests.DefaultAppendsAndPrependsSupportRequiredBoardCapacity'
LABEL='StrataAI.Domain.Tests.CardLabelTransferPlanTests.PRD_07_LargeContainedReferenceSetKeepsEveryCardAndOneSharedDefinition'


def validate(report, root, deferred):
    expected_sources={'tests/StrataAI.Domain.Tests/RankTokenTests.cs','tests/StrataAI.Domain.Tests/CardLabelTransferPlanTests.cs'}
    if not isinstance(deferred.get('sources'),list) or len(deferred['sources'])!=2 or {s.get('source') for s in deferred['sources']}!=expected_sources:
        raise ValueError('Original Domain deferral source inventory required')
    for source in deferred['sources']:
        if hashlib.sha256((Path(root)/source['source']).read_bytes()).hexdigest()!=source['sha256']:
            raise ValueError('Original Domain load deferrals changed; review source')
    text=Path(report).read_text()
    if '<!DOCTYPE' in text.upper() or '<!ENTITY' in text.upper():raise ValueError('Unexpected XML declarations')
    document=ET.fromstring(text)
    if document.tag!='{'+NS['t']+'}TestRun':raise ValueError('Expected Domain TRX')
    definitions={}
    for row in document.findall('t:TestDefinitions/t:UnitTest',NS):
        method=row.find('t:TestMethod',NS);identity=row.get('id')
        if not identity or identity in definitions or method is None:raise ValueError('Invalid Domain definition')
        definitions[identity]=method.get('className','').split(',',1)[0]+'.'+method.get('name','')
    cases=document.findall('t:Results/t:UnitTestResult',NS)
    executions=set();skipped=Counter();passed=0
    for case in cases:
        identity=case.get('executionId');method=definitions.get(case.get('testId'))
        if not identity or identity in executions or not method or not method.startswith('StrataAI.Domain.Tests.'):raise ValueError('Invalid Domain execution')
        executions.add(identity)
        if case.get('outcome')=='Passed':passed+=1;continue
        if case.get('outcome')!='NotExecuted':raise ValueError('Domain failure/error')
        if method==RANK:
            count=re.search(r'count:\s*(200|5000)\b',case.get('testName',''))
            if not count:raise ValueError('Unknown Domain capacity row')
            skipped[(method,count[1])]+=1
        elif method==LABEL:skipped[(method,'')]+=1
        else:raise ValueError('New Domain skip requires review')
    expected=Counter({(RANK,'200'):1,(RANK,'5000'):1,(LABEL,''):1})
    if not passed or skipped!=expected:raise ValueError('Missing complete Domain results or original deferrals')
    summary=document.find('t:ResultSummary',NS)
    if summary is None or summary.get('outcome')!='Completed':raise ValueError('Incomplete Domain run')
    counters=summary.find('t:Counters',NS)
    for field,expected_count in {'total':len(cases),'passed':passed,'executed':passed,'failed':0,'notExecuted':3}.items():
        if counters is None or int(counters.get(field,'-1'))!=expected_count:raise ValueError('Inconsistent Domain counters')
    return {'result':'Passed','passed':passed,'existingDeferred':3,'productionQualified':False}


if __name__=='__main__':
    report,root,deferred,output=sys.argv[1:]
    Path(output).write_text(json.dumps(validate(report,root,json.loads(Path(deferred).read_text())),indent=2)+'\n')
