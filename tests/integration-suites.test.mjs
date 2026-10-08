import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { readWorkflow, verifyIntegrationSuites } from '../scripts/ci/verify-integration-suites.mjs';

const source = readFileSync('.github/workflows/ci.yml', 'utf8');
const registry = JSON.parse(readFileSync('scripts/ci/integration-suites.json', 'utf8'));
const workflow = () => readWorkflow(source);
const integration = value => value.jobs['container-integration'];
const step = (value, name) => integration(value).steps.find(entry => entry.name === name);

test('every mandatory check has an owner and every group uses retained images', () => {
  assert.deepEqual(verifyIntegrationSuites(workflow(), registry), { groups: 4, registeredSteps: 94 });
});

const mutations = [
  ['omitted native capacity test', value => { integration(value).steps = integration(value).steps.filter(entry => entry.name !== 'Check concurrent and large-board rank allocation'); }],
  ['removed full browser group', value => { integration(value).strategy.matrix.suite.pop(); }],
  ['fail-fast cancellation', value => { integration(value).strategy['fail-fast'] = true; }],
  ['ignored integration failure', value => { integration(value)['continue-on-error'] = true; }],
  ['ignored mandatory test failure', value => { step(value, 'Authenticated browser E2E against exact release images')['continue-on-error'] = true; }],
  ['check assigned to wrong group', value => { step(value, 'Check concurrent and large-board rank allocation').if = "(matrix.suite == 'commands')"; }],
  ['mail keys missing from full browser prerequisite', value => { step(value, 'Prepare isolated identity mail transport and ephemeral signing keys').if = "(matrix.suite == 'browser-notifications')"; }],
  ['identity mail runs before provider setup', value => {
    const steps = integration(value).steps;
    const index = steps.findIndex(entry => entry.name === 'Prepare isolated identity mail transport and ephemeral signing keys');
    const [provider] = steps.splice(index, 1); steps.splice(steps.length - 1, 0, provider);
  }],
  ['shared artifact name collision', value => { step(value, 'Upload integration diagnostics').with.name = 'shared-diagnostics'; }],
  ['downstream source rebuild', value => { step(value, 'Load exact built images').run += '\ndocker build -t replacement .'; }],
  ['different image artifact', value => { step(value, 'Download exact built images').with.name = 'other-images'; }],
  ['omitted archive verification', value => { step(value, 'Verify image archive checksums').run = 'echo unchecked'; }],
  ['missing retained Worker image', value => { step(value, 'Load exact built images').run = step(value, 'Load exact built images').run.replace('gunzip -c image-artifacts/strataai-worker.tar.gz | docker load', 'echo skipped'); }],
  ['aggregate loses mandatory matrix dependency', value => { value.jobs['required-ci'].needs = ['source-quality-gate', 'build-images-once', 'security']; }],
  ['aggregate ignores matrix result', value => { value.jobs['required-ci'].steps[0].env.CONTAINER_RESULT = 'success'; }],
  ['aggregate accepts non-success result', value => { value.jobs['required-ci'].steps[0].run = 'echo success'; }],
  ['conditional cleanup', value => { step(value, 'Stop release topology').if = 'failure()'; }],
  ['metadata failure ignored by source gate', value => { value.jobs['source-quality-gate'].steps[0].env.METADATA_RESULT = 'success'; }],
  ['source checks omit metadata', value => { value.jobs['web-quality'].needs = []; }],
  ['build version differs from initial metadata', value => { value.jobs['build-images-once'].env.STRATAAI_BUILD_VERSION = '0.1.0-other'; }],
  ['image arguments recalculate build version', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Build web image').run = 'docker build --build-arg STRATAAI_BUILD_VERSION=other .'; }],
  ['image archive metadata lacks checksums', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Export exact built images').run = 'echo unchecked'; }],
  ['release metadata reconstructed downstream', value => { value.jobs['release-bundle'].steps.find(entry => entry.name === 'Assemble release bundle').run = 'cat > bundle/build-metadata.json'; }],
  ['security archive checksums bypassed', value => { value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity').run = 'cp image-artifacts/build-metadata.json security-artifacts/build-metadata.json'; }],
  ['security verification moved after loading', value => { const steps = value.jobs.security.steps; const index = steps.findIndex(entry => entry.name === 'Verify security input integrity and retain build identity'); steps.push(...steps.splice(index, 1)); }],
  ['security identity silently omitted', value => { const entry = value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity'); entry.run = entry.run.replace('cp image-artifacts/build-metadata.json security-artifacts/build-metadata.json', 'echo omitted'); }],
  ['security verification failure ignored', value => { value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity')['continue-on-error'] = true; }],
  ['security version diverges from metadata', value => { value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity').env.STRATAAI_BUILD_VERSION = 'other'; }],
  ['raw assertion reports published', value => { value.jobs['dotnet-quality'].steps.find(entry => entry.name === 'Retain .NET source test results').with.path = '${{ runner.temp }}/source-tests-raw'; }],
  ['source test failure summary skipped', value => { value.jobs['web-quality'].steps.find(entry => entry.name === 'Summarize web source test results').if = 'success()'; }],
  ['source report publication failure ignored', value => { value.jobs['web-quality'].steps.find(entry => entry.name === 'Summarize web source test results')['continue-on-error'] = true; }],
  ['mandatory API source suite filtered', value => { value.jobs['dotnet-quality'].steps.find(entry => entry.name === 'API host tests').run += ' --filter-method *Notification*'; }],
  ['source reporter dropped', value => { value.jobs['web-quality'].steps.find(entry => entry.name === 'Unit and component tests').run = 'npm test'; }],
];
for (const [name, mutate] of mutations) {
  test(`rejects ${name}`, () => {
    const value = workflow(); mutate(value);
    assert.throws(() => verifyIntegrationSuites(value, registry));
  });
}

test('duplicate YAML keys are rejected before coverage validation', () => {
  assert.throws(() => readWorkflow('jobs:\n  required-ci: {}\n  required-ci: {}\n'));
});
