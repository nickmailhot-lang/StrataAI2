import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import test from 'node:test';
import { progress, transition, validateLedger, validateBaseline, validateEvolution } from '../scripts/delivery-progress.mjs';

const revision = 'a'.repeat(40), at = '2026-10-17T15:00:00Z';
const gateNames = ['tests','acceptance','security','roles','regressions','integration','tracking','review','ux','build'];
const states = ['Ready','In Progress','Implemented','Testing','Review','Done'];
function fixture(done = false) {
  const items = ['Epic','PRD','Vertical Slice','Task','Test'].map((kind,i) => ({
    id: `ITEM-${i}`, parentId:i ? `ITEM-${i-1}` : null, kind, title:kind, category:kind==='PRD'?'feature':undefined,
    state:done?'Done':'Ready', required:true, mvpCritical:null, estimate:null, dependencies:[],
    acceptanceCriteria:[`ITEM-${i}/AC`],
    history:(done?states:['Ready']).map((state,j) => ({at,from:j?states[j-1]:null,to:state,reason:'Fixture transition evidence'})),
    ...(kind==='Test'?{verifies:['ITEM-2/AC'],testResult:done?'Passed':'Not Run'}:{}),
    ...(done?{implementation:{revision,paths:['app.txt']},knownCriticalRegressions:0,integration:{branch:'main',commit:revision},gates:Object.fromEntries(gateNames.map(name=>[name,{status:'Passed',evidenceIds:['RESULT-1']}]))}:{})
  }));
  return {schemaVersion:1,baselineRevision:revision,targetBranch:'main',items,
    criteria:items.map(x=>({id:x.acceptanceCriteria[0],ownerId:x.id,sourceId:'AC',text:'Demonstrate persisted result and permitted return',status:done?'Verified':'Unverified',evidenceIds:done?['RESULT-1']:[],...(done?{reviewer:'fixture-reviewer',verifiedAt:at}:{})})),
    evidence:done?[{id:'RESULT-1',revision,result:'Passed',command:'fixture test',at,path:'pass.txt',sha256:createHash('sha256').update('passed').digest('hex')}]:[],cycles:[]};
}
const clone = value => structuredClone(value);
const ledger = JSON.parse(readFileSync('docs/delivery/progress.json','utf8'));
const snapshot = JSON.parse(readFileSync('docs/delivery/requirements-snapshot.json','utf8'));
const audit = JSON.parse(readFileSync('docs/delivery/baseline-audit.json','utf8'));

test('PRD-25-VS-03/AC-01: complete real backlog retains all 100 definitions, obligations and parent links', () => {
  validateLedger(ledger, {root:resolve('.')}); validateBaseline(ledger,snapshot,audit);
  assert.equal(progress(ledger).totalPrds,88); assert.equal(progress(ledger).canonicalFeaturePrds,80);
});
test('PRD-25-VS-03/AC-01: losing a PRD or architecture definition fails closed', () => {
  for (const id of ['PRD-01','PRD-80','ARCH-01','UX-008']) {
    const d=clone(ledger); d.items=d.items.filter(x=>x.id!==id);
    assert.throws(()=>validateBaseline(d,snapshot,audit),/canonical definition/);
  }
});
test('PRD-25-VS-03/AC-01: losing original FR or AC cannot shrink acceptance denominator', () => {
  const d=clone(ledger), prd=d.items.find(x=>x.id==='PRD-01'); prd.acceptanceCriteria.pop();
  d.criteria=d.criteria.filter(c=>c.ownerId!=='PRD-01'||prd.acceptanceCriteria.includes(c.id));
  assert.throws(()=>validateBaseline(d,snapshot,audit),/source obligations/);
});
test('PRD-25-VS-03/AC-01: source mutation cannot inherit frozen audit', () => {
  const s=clone(snapshot); s.definitions[0].body+=' changed';
  assert.throws(()=>validateBaseline(ledger,s,audit),/Changed source body/);
});
test('PRD-25-VS-03/AC-01: missing slice requirement mapping is rejected', () => {
  const d=clone(ledger); d.items.filter(x=>x.parentId==='PRD-01').forEach(x=>x.scopeRefs=[]);
  assert.throws(()=>validateBaseline(d,snapshot,audit),/missing from slices/);
});
test('PRD-25-VS-03/AC-02: duplicate item IDs, orphan items and acceptance are refused', () => {
  for (const change of [d=>d.items.push(clone(d.items[0])),d=>d.items[3].parentId='missing',d=>d.criteria[0].ownerId='missing']) {
    const d=fixture(); change(d); assert.throws(()=>validateLedger(d));
  }
});
test('PRD-25-VS-03/AC-02: untraced test and unknown dependencies are refused', () => {
  const d=fixture(); d.items[4].verifies=['missing']; assert.throws(()=>validateLedger(d),/Unmapped test/);
  d.items[4].verifies=['ITEM-2/AC']; d.items[2].dependencies=['missing']; assert.throws(()=>validateLedger(d),/dependency/);
});
test('PRD-25-VS-03/AC-02: lifecycle runs in order and does not skip validation', () => {
  const d=fixture(); assert.throws(()=>transition(d,'ITEM-3','Done','Code exists'),/Illegal/);
  const changed=transition(d,'ITEM-3','In Progress','Focused cycle started'); validateLedger(changed);
  assert.equal(changed.items[3].state,'In Progress'); assert.equal(d.items[3].state,'Ready');
});
test('PRD-25-VS-03/AC-02: Blocked requires reason, owner and exact prior-state resume', () => {
  const blocked=transition(fixture(),'ITEM-3','Blocked','Database fixture is unavailable',at,'fixture-owner'); validateLedger(blocked);
  assert.throws(()=>transition(blocked,'ITEM-3','Review','Skip validation'),/Illegal/);
  const resumed=transition(blocked,'ITEM-3','Ready','Fixture restored'); validateLedger(resumed);
  assert.equal(resumed.items[3].blocked,undefined);
  delete blocked.items[3].blocked.reason; assert.throws(()=>validateLedger(blocked),/Blocked item/);
});
test('PRD-25-VS-03/AC-02: Implemented needs source identity, never just a state label', () => {
  const d=transition(transition(fixture(),'ITEM-3','In Progress','Started'),'ITEM-3','Implemented','Code present');
  assert.throws(()=>validateLedger(d),/source revision/);
});
test('PRD-25-VS-03/AC-02: state and append-only history must agree', () => {
  const d=fixture(); d.items[3].state='In Progress'; assert.throws(()=>validateLedger(d),/history mismatch/);
});
test('PRD-25-VS-03/AC-02: a new item cannot start at Done to bypass lifecycle', () => {
  const d=fixture(true); d.items[4].history=[{at,from:null,to:'Done',reason:'Skip all lifecycle states'}];
  assert.throws(()=>validateLedger(d),/initial history/);
});
test('PRD-25-VS-03/AC-03: a Test cannot claim Passed without executed evidence', () => {
  const d=fixture(); d.items[4].testResult='Passed'; assert.throws(()=>validateLedger(d),/Unsubstantiated test pass/);
});
test('PRD-25-VS-03/AC-03: complete synthetic evidence passes all gates', () => validateLedger(fixture(true)));
for (const gate of gateNames) test(`PRD-25-VS-03/AC-03: missing ${gate} blocks Done`, () => {
  const d=fixture(true); delete d.items[3].gates[gate]; assert.throws(()=>validateLedger(d),/gate/);
});
test('PRD-25-VS-03/AC-03: Done parent cannot conceal incomplete required child', () => {
  const d=fixture(true); d.items[4]=fixture().items[4]; d.criteria[4]=fixture().criteria[4]; assert.throws(()=>validateLedger(d),/Required child/);
});
test('PRD-25-VS-03/AC-03: unverified, stale, failed or skipped evidence cannot satisfy acceptance', () => {
  for (const change of [d=>d.criteria[0].status='Unverified',d=>d.evidence[0].revision='b'.repeat(40),d=>d.evidence[0].result='Failed',d=>d.evidence[0].result='Skipped']) {
    const d=fixture(true); change(d); assert.throws(()=>validateLedger(d));
  }
});
test('PRD-25-VS-03/AC-03: unknown critical regressions and unpassed test block completion', () => {
  const d=fixture(true); d.items[4].knownCriticalRegressions=null; assert.throws(()=>validateLedger(d),/critical regressions/);
  d.items[4].knownCriticalRegressions=0; d.items[4].testResult='Skipped'; assert.throws(()=>validateLedger(d),/Unpassed Test/);
});
test('PRD-25-VS-03/AC-03: only reasoned inapplicability is allowed, mandatory gates cannot be waived', () => {
  const d=fixture(true); d.items[4].gates.ux={status:'Not Applicable',reason:'CLI-only system slice with no browser or product UI changes.'}; validateLedger(d);
  d.items[3].gates.tests={status:'Not Applicable',reason:'Attempt to bypass all the mandatory tests.'}; assert.throws(()=>validateLedger(d),/gate tests/);
});
test('PRD-25-VS-03/AC-04: counts separate hierarchy, tests, unknown MVP and unvalidated work', () => {
  const d=fixture(); d.items[3].mvpCritical=true;
  let report=progress(d); assert.equal(report.totalWorkItems,5); assert.equal(report.workItemsCompleted,0); assert.equal(report.byKind.Test.total,1);
  assert.equal(report.mvpClassificationUnknown,4); assert.deepEqual(report.mvpCriticalOutstanding,['ITEM-3']); assert.equal(report.weightedProgress,null);
  d.items[3].state='Implemented'; d.items[3].implementation={revision,paths:['app.txt']}; assert.deepEqual(progress(d).implementedButUnvalidated,['ITEM-3']);
  d.items[3].state='Blocked';d.items[3].blocked={reason:'CI pending',owner:'delivery-owner'};assert.deepEqual(progress(d).implementedButUnvalidated,['ITEM-3']);
});
test('PRD-25-VS-03/AC-04: closed source issues and historic results do not become Done', () => {
  const incomplete=fixture();
  incomplete.items[1].source={issueState:'closed'};
  incomplete.historicalEvidence=clone(ledger.historicalEvidence);
  validateLedger(incomplete);
  const report=progress(incomplete); assert.equal(report.prdsCompleted,0); assert.equal(report.workItemsCompleted,0);
  assert.equal(report.recentCompletedIncrements.length,0); assert.deepEqual(report.historicalEvidence,ledger.historicalEvidence);
  assert.ok(report.historicalEvidence.length>0);
  assert.equal(ledger.items.find(x=>x.id==='ARCH-01').source.issueState,'closed');
});

test('PRD-25-VS-03/AC-04: validated Done work contributes completion counts and recent increments', () => {
  const complete=fixture(true); validateLedger(complete);
  const report=progress(complete);
  assert.equal(report.prdsCompleted,1); assert.equal(report.workItemsCompleted,5);
  for (const kind of ['Epic','PRD','Vertical Slice','Task','Test']) assert.equal(report.byKind[kind].done,1);
  assert.equal(report.recentCompletedIncrements.length,1);
  assert.equal(report.recentCompletedIncrements[0].id,'ITEM-2');
  assert.equal(report.recentCompletedIncrements[0].completedAt,at);
  assert.equal(report.weightedProgress,null);
});
test('PRD-25-VS-03/AC-05: prior states, criteria and failed evidence cannot be rewritten or deleted', () => {
  const old=fixture(true);
  for (const change of [d=>d.items.pop(),d=>d.items[0].history[0].reason='rewrite',d=>d.criteria.pop(),d=>d.evidence.pop(),d=>d.items[3].required=false]) {
    const d=clone(old); change(d); assert.throws(()=>validateEvolution(old,d));
  }
  validateEvolution(old,transition(old,'ITEM-0','In Progress','Reopen acceptance after regression'));
});
test('PRD-25-VS-03/AC-03: exact Git integration, evidence hashes and repository path boundaries', () => {
  const folder=mkdtempSync(join(tmpdir(),'strataai-delivery-'));
  const git=(...args)=>execFileSync('git',args,{cwd:folder,encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim();
  try {
    git('init','-b','main'); git('config','user.email','fixture@example.invalid'); git('config','user.name','Fixture');
    writeFileSync(join(folder,'app.txt'),'baseline'); writeFileSync(join(folder,'pass.txt'),'passed'); git('add','.'); git('commit','-m','fixture baseline');
    const d=fixture(true), commit=git('rev-parse','HEAD');
    d.evidence[0].revision=commit; d.items.forEach(x=>{x.implementation.revision=commit;x.integration.commit=commit;}); validateLedger(d,{root:folder});
    writeFileSync(join(folder,'pass.txt'),'tampered'); assert.throws(()=>validateLedger(d,{root:folder}),/artifact missing or changed/);
    writeFileSync(join(folder,'pass.txt'),'passed');
    for (const path of ['../private.txt','C:\\private.txt','/private.txt']) {
      d.evidence[0].path=path; assert.throws(()=>validateLedger(d,{root:folder}),/Unsafe|escapes/);
    }
    d.evidence[0].path='pass.txt'; git('checkout','-b','feature'); writeFileSync(join(folder,'app.txt'),'feature'); git('add','.');git('commit','-m','unintegrated');
    const feature=git('rev-parse','HEAD');d.evidence[0].revision=feature;d.items.forEach(x=>{x.implementation.revision=feature;x.integration.commit=feature;});
    assert.throws(()=>validateLedger(d,{root:folder}),/not in main/);
  } finally {
    assert.equal(dirname(realpathSync(folder)),realpathSync(tmpdir())); assert.ok(basename(folder).startsWith('strataai-delivery-'));
    rmSync(folder,{recursive:true,force:true});
  }
});
test('PRD-25-VS-03/AC-04: real CLI reports machine-readable progress and exits nonzero for bad action', () => {
  const result=spawnSync(process.execPath,['scripts/delivery-progress.mjs','report','--json'],{encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);assert.equal(JSON.parse(result.stdout).totalPrds,88);
  const invalid=spawnSync(process.execPath,['scripts/delivery-progress.mjs','transition','PRD-01','Done','Code exists'],{encoding:'utf8'});
  assert.equal(invalid.status,1); assert.match(invalid.stderr,/Illegal/);
});
test('PRD-25-VS-03/AC-06: required metadata job runs ledger validation and its refusal tests', () => {
  const workflow=readFileSync('.github/workflows/full-sanity.yml','utf8');
  const metadata=workflow.split(/\n  web-quality:/)[0];
  assert.match(metadata,/node scripts\/delivery-progress\.mjs validate/);
  assert.match(metadata,/node --test tests\/delivery-progress\.test\.mjs/);
});

test('PRD-25-VS-03/AC-05: CLI can validate evolution against the actual retained ledger in Git', () => {
  const result=spawnSync(process.execPath,['scripts/delivery-progress.mjs','validate','--base-ref','HEAD'],{encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);
  assert.match(result.stdout,/No unsupported Done claims/);
});
