import test from 'node:test';
import assert from 'node:assert/strict';
import { createPlan } from './plan.mjs';
import { requireGateResults } from './aggregate.mjs';

test('final frontend plan binds actual dependent files and source revision into strict aggregate', () => {
  const plan = createPlan(['apps/web/src/features/auth/AuthPage.tsx'], '.');
  assert.ok(plan.webTests.includes('apps/web/src/features/auth/AuthPage.landmark.test.tsx'));
  assert.equal(plan.apiSelection, null);
  assert.equal(requireGateResults({plan,results:{contracts:'success',web:'success',dotnet:'skipped',api:'skipped',full:'skipped'},expectedScopeFingerprint:plan.scopeFingerprint,expectedHead:plan.revision,observedHead:plan.revision,startedAt:'2026-11-01T00:00:00Z',finishedAt:'2026-11-01T00:05:00Z'}).productionQualified, false);
});
test('backend plan binds complete reviewed HTTP methods and retains Domain compile lane', () => {
  const plan = createPlan(['src/StrataAI.Api/Program.cs'], '.');
  assert.equal(plan.dotnet, true); assert.equal(plan.api, true);
  assert.ok(plan.apiSelection.methods.length > 10);
  assert.equal(plan.apiSelection.theoryRows, 'all');
  assert.equal(plan.full, false);
  assert.deepEqual(createPlan(['src/StrataAI.Api/Program.cs','src/StrataAI.Api/Program.cs'],'.'),plan);
});
test('nightly and exact candidate plans retain full suite without treating empty fast scopes as qualification', () => {
  const plan = createPlan([], '.', {event:'production-candidate'});
  assert.equal(plan.tier, 'full'); assert.equal(plan.full, true);
  assert.deepEqual(plan.webTests, []); assert.equal(plan.apiSelection, null);
});
test('manual backend/cold measurement adds checks without hiding actual frontend scope or permitting PR overrides',()=>{
  const paths=['apps/web/src/features/auth/AuthPage.tsx'];
  const measured=createPlan(paths,'.',{event:'manual',measureBackend:true,coldCache:true});
  assert.equal(measured.web,true);assert.equal(measured.dotnet,true);assert.ok(measured.webTests.length);assert.ok(measured.apiSelection.methods.length);assert.deepEqual(measured.changedPaths,paths);
  assert.throws(()=>createPlan(paths,'.',{measureBackend:true}),/manual/);
  assert.throws(()=>createPlan(paths,'.',{event:'manual',forceFull:true,coldCache:true}),/full qualification/);
});
