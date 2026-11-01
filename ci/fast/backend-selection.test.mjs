import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { selectBackendTests, baselineApi } from './backend-selection.mjs';
const folder = 'tests/StrataAI.Api.Tests/';
const inventory = readdirSync(folder).map(f => folder + f);
const read = file => readFileSync(file, 'utf8');
test('composed API retains complete original HTTP authority and Problem baseline methods', () => {
  const result = selectBackendTests(['src/StrataAI.Api/Program.cs'], inventory, read);
  assert.deepEqual(result.files, baselineApi.map(f => folder + f).sort());
  assert.equal(result.theoryRows, 'all');
  assert.ok(result.methods.some(m => m.endsWith('Production_without_database_configuration_fails_closed')));
});
test('Board and authentication source select reviewed HTTP consumers alongside baseline', () => {
  const result = selectBackendTests(['src/StrataAI.Application/BoardCommands.cs','src/StrataAI.Api/IdentityEndpoints.cs'], inventory, read);
  for (const file of ['BoardCreationActorTests.cs','IdentityLoginReplayTests.cs']) assert.ok(result.files.includes(folder + file));
});
test('changed API tests retain their whole original file rather than a class or individual row', () => {
  const file = folder + 'OrganizationSettingsActorTests.cs';
  assert.ok(selectBackendTests([file], inventory, read).files.includes(file));
});
test('unknown consumers and missing reviewed anchors refuse selection', () => {
  assert.throws(() => selectBackendTests(['src/StrataAI.Application/UnclassifiedConsumer.cs'], inventory, read), /review/);
  assert.throws(() => selectBackendTests(['src/StrataAI.Api/Program.cs'], [], read), /Unreviewed/);
});
test('specific date and Organization authority consumers precede generic Board/auth tokens',()=>{
  const result=selectBackendTests(['src/StrataAI.Application/BoardDatePolicy.cs','src/StrataAI.Worker/OrganizationAuthorityWorker.cs'],inventory,read);
  assert.ok(result.files.includes(folder+'CardDateTests.cs'));
  assert.ok(result.files.includes(folder+'OrganizationSettingsActorTests.cs'));
  assert.ok(!result.files.includes(folder+'IdentityLoginReplayTests.cs'));
});
