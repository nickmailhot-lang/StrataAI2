import test from 'node:test';import assert from 'node:assert/strict';
import { selectFeatureGate } from './selection.mjs';import { requireGateResults } from './aggregate.mjs';
const inventory=['tests/browser/board.spec.ts'];const head='1'.repeat(40);
function input(paths=['apps/web/src/features/organizations/OrganizationHome.tsx'],options={}){
 const plan=selectFeatureGate(paths,inventory,options);
 return {plan,expectedHead:head,observedHead:head,expectedScopeFingerprint:plan.scopeFingerprint,
  results:{contracts:'success',web:!plan.full&&plan.web?'success':'skipped',dotnet:!plan.full&&plan.dotnet?'success':'skipped',api:!plan.full&&plan.api?'success':'skipped',full:plan.full?'success':'skipped'},startedAt:'2026-10-31T23:00:00.000Z',finishedAt:'2026-10-31T23:09:59.999Z'};
}
test('frontend pass requires successful web checks and explicit unselected backend results',()=>{
 const result=requireGateResults(input());assert.equal(result.result,'Passed');assert.equal(result.productionQualified,false);
});
for(const name of ['contracts','web'])for(const result of ['failure','cancelled','timed_out','skipped',''])
 test(`required ${name}=${result} cannot qualify a feature`,()=>{const value=input();value.results[name]=result;assert.throws(()=>requireGateResults(value),/result mismatch/)});
test('backend feature cannot skip API or compile checks',()=>{
 for(const name of ['dotnet','api']){const value=input(['src/StrataAI.Api/Program.cs']);value.results[name]='skipped';assert.throws(()=>requireGateResults(value),/result mismatch/)}
});
test('unexpected executed full job cannot masquerade as feature qualification',()=>{const value=input();value.results.full='success';assert.throws(()=>requireGateResults(value),/result mismatch/)});
test('missing and unknown jobs fail closed',()=>{const value=input();delete value.results.dotnet;assert.throws(()=>requireGateResults(value));value.results.extra='success';assert.throws(()=>requireGateResults(value))});
test('changed source and scope refuse reuse',()=>{const value=input();assert.throws(()=>requireGateResults({...value,observedHead:'2'.repeat(40)}),/revision/);assert.throws(()=>requireGateResults({...value,expectedScopeFingerprint:'2'.repeat(64)}),/scope/)});
test('tampered selection cannot hide an originally required job behind the original digest',()=>{
 const value=input();value.plan.web=false;value.results.web='skipped';assert.throws(()=>requireGateResults(value),/scope was modified/);
});
test('ten minutes and missing or reversed timing fail, rather than dropping tests',()=>{
 for(const finishedAt of ['2026-10-31T23:10:00.000Z','2026-10-31T23:10:00.001Z','2026-10-31T22:59:00Z','',undefined])assert.throws(()=>requireGateResults({...input(),finishedAt}));
});
test('documentation keeps contract checks while optional jobs remain explicitly unselected',()=>assert.equal(requireGateResults(input(['docs/example.md'])).profile,'docs'));
test('candidate full workflow remains separately required and cannot emit production receipt here',()=>{
 const value=input(['src/StrataAI.Api/Program.cs'],{event:'production-candidate'});value.finishedAt='2026-11-01T01:00:00.000Z';assert.equal(requireGateResults(value).productionQualified,false);value.results.full='skipped';assert.throws(()=>requireGateResults(value));
});
