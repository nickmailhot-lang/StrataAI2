import test from 'node:test';
import assert from 'node:assert/strict';
import { createPlan } from './plan.mjs';
import { reusable, completeChecks } from '../../scripts/ci/automatic-qualification.mjs';
const plan=createPlan(['src/StrataAI.Api/Program.cs'],'.');
const request={repository:'owner/repo',commit:plan.revision,tree:plan.sourceTree,base:'c'.repeat(40),source:plan.revision,policy:'e'.repeat(64),scope:plan};
const run={id:100,workflow_id:10,run_attempt:1,path:'.github/workflows/feature-ci.yml',head_sha:request.source,event:'pull_request',status:'completed',conclusion:'success'};
const current={runId:'200',workflowId:10};
const proof={schemaVersion:2,result:'success',reused:false,runId:100,runAttempt:1,identity:request,elapsedMs:100_000,productionQualified:false};
const env={SELECTION_RESULT:'success',FULL_SELECTED:'false',WEB_SELECTED:'false',RUNTIME_SELECTED:'false',BACKEND_SELECTED:'true',WEB_RESULT:'skipped',RUNTIME_RESULT:'skipped',FULL_RESULT:'skipped',BACKEND_RESULT:'success',REUSED:'false',FAST_CI_STARTED_AT:'2026-11-01T00:00:00Z',FAST_CI_FINISHED_AT:'2026-11-01T00:05:00Z'};
test('fresh complete backend evidence qualifies only the exact selected development scope',()=>{
  assert.equal(completeChecks(env,request,undefined,undefined,current).productionQualified,false);
  assert.throws(()=>completeChecks({...env,BACKEND_RESULT:'skipped'},request,undefined,undefined,current));
  assert.throws(()=>completeChecks({...env,BACKEND_SELECTED:'false'},request,undefined,undefined,current));
});
test('matching new-profile proof can be reused only from the original complete successful run',()=>{
  assert.equal(reusable(request,proof,run,current),true);
  completeChecks({...env,REUSED:'true',BACKEND_RESULT:'skipped'},request,proof,run,current);
  assert.throws(()=>completeChecks({...env,REUSED:'true'},request,proof,run,current));
});
test('all new backend, smoke, compile, file and method scope fields prevent stale reuse',()=>{
  for(const changed of [{profile:'docs'},{dotnet:false},{api:false},{webTests:['different.test.ts']},{apiSelection:{...plan.apiSelection,methods:[...plan.apiSelection.methods,'Different.Method']}},{sourceTree:'f'.repeat(40)},{scopeFingerprint:'0'.repeat(64)}])
    assert.equal(reusable({...request,scope:{...plan,...changed}},proof,run,current),false);
});
test('legacy, missing-timing and over-budget evidence cannot qualify the new fast profile',()=>{
  for(const change of [{schemaVersion:1},{elapsedMs:undefined},{elapsedMs:600_000},{elapsedMs:-1},{reused:true}])assert.equal(reusable(request,{...proof,...change},run,current),false);
  for(const time of ['2026-11-01T00:10:00Z','invalid'])assert.throws(()=>completeChecks({...env,FAST_CI_FINISHED_AT:time},request,undefined,undefined,current));
});
