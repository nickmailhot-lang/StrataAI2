import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { parse } from 'yaml';
import { automaticRequest, reusable, findReusable, qualificationKey, completeChecks } from '../scripts/ci/automatic-qualification.mjs';
import { selectImpact } from '../scripts/ci/select-feature-tests.mjs';
import { doctor } from '../scripts/local/doctor.mjs';

const request = { repository:'owner/repo', commit:'a'.repeat(40), tree:'b'.repeat(40), base:'c'.repeat(40), source:'d'.repeat(40), policy:'e'.repeat(64), scope:{tier:'full',full:true,web:true,runtime:false,browser:['tests/browser/board.spec.ts'],commands:['roles','tenant']} };
const current = { runId:'200', workflowId:10 };
const run = { id:100, workflow_id:10, run_attempt:1, path:'.github/workflows/feature-ci.yml', head_sha:request.source, event:'pull_request', status:'completed', conclusion:'success' };
const proof = { schemaVersion:1, result:'success', reused:false, runId:100, runAttempt:1, identity:request };
const env = { SELECTION_RESULT:'success', FULL_SELECTED:'true', WEB_SELECTED:'true', RUNTIME_SELECTED:'false', WEB_RESULT:'skipped', RUNTIME_RESULT:'skipped', FULL_RESULT:'skipped', REUSED:'true' };
test('equivalent completed qualification can satisfy the unchanged automatic request', () => {
  assert.equal(reusable(request,proof,run,current),true);
  completeChecks(env,request,proof,run,current);
});
for (const field of ['repository','commit','tree','base','source','policy']) test(`fresh work required after ${field} changes`, () => {
  assert.equal(reusable({...request,[field]:'changed'},proof,run,current),false);
});
for (const scope of [{...request.scope,tier:'feature',full:false},{...request.scope,browser:[...request.scope.browser,'new.spec.ts']},{...request.scope,commands:['roles','tenant','extra']}]) test(`coverage must match: ${JSON.stringify(scope)}`, () => {
  assert.equal(reusable({...request,scope},proof,run,current),false);
});
for (const conclusion of ['failure','cancelled',null,'skipped','timed_out']) test(`never accept ${conclusion} as qualification`, () => {
  assert.equal(reusable(request,proof,{...run,conclusion},current),false);
});
for (const changed of [{status:'in_progress'},{event:'workflow_dispatch'},{workflow_id:11},{path:'other.yml'},{head_sha:'other'},{run_attempt:2},{id:200}]) test(`reject wrong run provenance ${JSON.stringify(changed)}`, () => {
  assert.equal(reusable(request,proof,{...run,...changed},current),false);
});
for (const changed of [{reused:true},{result:'failure'},{runId:101},{schemaVersion:2},{runAttempt:2}]) test(`reject invalid proof ${JSON.stringify(changed)}`, () => {
  assert.equal(reusable(request,{...proof,...changed},run,current),false);
});
test('manual, retry, merge queue, nightly and tag qualification remain fresh', () => {
  assert.equal(automaticRequest({GITHUB_EVENT_NAME:'pull_request',GITHUB_RUN_ATTEMPT:'1'}),true);
  for (const name of ['workflow_dispatch','schedule','push','merge_group']) assert.equal(automaticRequest({GITHUB_EVENT_NAME:name,GITHUB_RUN_ATTEMPT:'1'}),false);
  assert.equal(automaticRequest({GITHUB_EVENT_NAME:'pull_request',GITHUB_RUN_ATTEMPT:'2'}),false);
  assert.equal(automaticRequest({GITHUB_EVENT_NAME:'pull_request',GITHUB_RUN_ATTEMPT:'1'},{action:'labeled',label:{name:'full-ci'}}),false);
});
test('lookup paginates and checks original run before reading evidence', async () => {
  let reads=0; const artifact={id:20,name:`ci-qualification-${qualificationKey(request)}`,workflow_run:{id:100}};
  const calls=[];
  const result=await findReusable(request,current,async path=>{
    calls.push(path);
    if(path.includes('/actions/runs/')) return run;
    return {artifacts:path.endsWith('page=1')?Array.from({length:100},()=>({...artifact,expired:true})):[artifact]};
  },async()=>{reads++;return proof;});
  assert.deepEqual(result,{artifactId:20,runId:100});assert.equal(reads,1);assert.equal(calls.length,3);
  assert.equal(await findReusable(request,current,async path=>path.includes('/actions/runs/')?{...run,conclusion:'failure'}:{artifacts:[artifact]},async()=>{throw new Error('must not read failed evidence');}),null);
});
test('aggregator refuses stale/missing reuse and never accepts unexpectedly skipped selected jobs', () => {
  assert.throws(()=>completeChecks(env,request,{},run,current));
  assert.throws(()=>completeChecks({...env,FULL_RESULT:'success'},request,proof,run,current));
  assert.throws(()=>completeChecks({...env,SELECTION_RESULT:'failure'},request,proof,run,current));
  assert.throws(()=>completeChecks({...env,FULL_SELECTED:'false'},request,proof,run,current));
  assert.throws(()=>completeChecks({...env,REUSED:''},request,proof,run,current));
  assert.throws(()=>completeChecks({...env,REUSED:'false'},request,proof,run,current));
  completeChecks({...env,REUSED:'false',FULL_RESULT:'success'},request,undefined,undefined,current);
});
test('workflow retains stable strict aggregate and evidence-bound skips without ready event duplication', () => {
  const w=parse(readFileSync('.github/workflows/feature-ci.yml','utf8'));
  assert.ok(!w.on.pull_request.types.includes('ready_for_review'));
  for (const event of ['opened','synchronize','reopened','labeled','unlabeled']) assert.ok(w.on.pull_request.types.includes(event));
  for (const job of ['web','runtime','full']) assert.ok(w.jobs[job].if.includes("needs.select.outputs.reused == 'false'"));
  assert.equal(w.jobs['required-ci'].name,'required-ci');assert.equal(w.jobs['required-ci'].if,'always()');
  assert.equal(w.permissions.actions,'read');
  assert.ok(w.jobs.select.steps.some(s=>s.run==='node scripts/ci/automatic-qualification.mjs lookup'));
  assert.ok(w.jobs['required-ci'].steps.some(s=>s.run==='node scripts/ci/automatic-qualification.mjs complete'));
  assert.equal(w.jobs['required-ci'].steps.find(s=>s.name==='Retain successful qualification identity').if,"needs.select.outputs.reused == 'false'");
});
test('three tiers share conservative policy and include the persisted security baseline', () => {
  const inventory=readdirSync('tests/browser').filter(n=>n.endsWith('.spec.ts')).map(n=>`tests/browser/${n}`);
  assert.equal(selectImpact(['docs/evidence.json'],inventory).tier,'docs');
  const leaf=selectImpact(['apps/web/src/features/kanban/CardLabelsControl.tsx'],inventory);
  assert.equal(leaf.tier,'feature');assert.ok(leaf.browser.includes('tests/browser/board.spec.ts'));assert.equal(leaf.commands.length,2);
  for(const path of ['AGENTS.md','src/Label.cs','apps/web/src/features/kanban/cardpermissions.ts','scripts/ci/anything.mjs','unknown','tests/new.test.mjs']) assert.equal(selectImpact([path],inventory).tier,'full');
  assert.equal(selectImpact(['docs/evidence.json'],inventory,{forceFull:true}).tier,'full');
  assert.equal(selectImpact(['docs/evidence.json','db/migration.sql'],inventory).tier,'full');
  assert.throws(()=>selectImpact(['docs/evidence.json'],[]),/Missing baseline/);
});
test('docs doctor avoids unrelated SDK/build tools and still requires secret scanner engine', () => {
  const names=[];
  const launch=(tool,args)=>{names.push([tool,...args].join(' '));return {status:1,stdout:''};};
  doctor({tier:'docs',launch});
  assert.ok(names.some(n=>n.startsWith('docker version')));
  assert.ok(names.some(n=>n.startsWith('python3 ')));
  for(const tool of ['dotnet','openssl','jq','ln','sha256sum','timeout','wc']) assert.ok(!names.some(n=>n.startsWith(tool+' ')));
  names.length=0;doctor({tier:'full',launch});assert.ok(names.some(n=>n.startsWith('dotnet ')));
});
