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
  assert.deepEqual(verifyIntegrationSuites(workflow(), registry), { groups: 4, executions: 7, registeredSteps: 112 });
});

const mutations = [
  ['seeded-account Demo reminder execution omitted', value => {
    const entry = step(value, 'Demo metadata, terminal and recipient lifecycle against exact API and web images');
    entry.run = entry.run.split('\n').filter(line => !line.includes('demo-reminders-report.json')).join('\n');
  }],
  ['seeded-account reminder execution uses Production mode', value => {
    step(value, 'Demo metadata, terminal and recipient lifecycle against exact API and web images').env.STRATAAI_E2E_RUNTIME_MODE = 'production';
  }],
  ['startup refusal regressions omitted', value=>{const step=value.jobs['web-quality'].steps.find(step=>step.name==='Verify mandatory immutable integration coverage');step.run=step.run.replace('node --test tests/release-bundle-startup.test.mjs','');}],
  ['startup refusal regressions skipped', value=>{value.jobs['web-quality'].steps.find(step=>step.name==='Verify mandatory immutable integration coverage').if='false';}],
  ['startup refusal regressions permitted to fail', value=>{value.jobs['web-quality'].steps.find(step=>step.name==='Verify mandatory immutable integration coverage')['continue-on-error']=true;}],
  ['post-startup bundle verification replaced by success', value=>{value.jobs['release-bundle'].steps.find(step=>step.name==='Verify bundle unchanged after startup smoke').run='true';}],
  ['post-startup bundle verification moved after upload', value=>{const steps=value.jobs['release-bundle'].steps; const index=steps.findIndex(step=>step.name==='Verify bundle unchanged after startup smoke');steps.push(...steps.splice(index,1));}],
  ['post-startup bundle verification omitted', value=>{value.jobs['release-bundle'].steps=value.jobs['release-bundle'].steps.filter(step=>step.name!=='Verify bundle unchanged after startup smoke');}],
  ['post-startup bundle verification skipped', value=>{value.jobs['release-bundle'].steps.find(step=>step.name==='Verify bundle unchanged after startup smoke').if='false';}],
  ['post-startup bundle verification failure ignored', value=>{value.jobs['release-bundle'].steps.find(step=>step.name==='Verify bundle unchanged after startup smoke')['continue-on-error']=true;}],
  ['post-startup bundle verification moved before smoke', value=>{const steps=value.jobs['release-bundle'].steps; const index=steps.findIndex(step=>step.name==='Verify bundle unchanged after startup smoke');const [entry]=steps.splice(index,1); steps.splice(steps.findIndex(step=>step.name==='Verify assembled bundle startup without source or SDKs'),0,entry);}],
  ['assembled bundle startup omitted', value => { value.jobs['release-bundle'].steps=value.jobs['release-bundle'].steps.filter(step=>step.name!=='Verify assembled bundle startup without source or SDKs'); }],
  ['assembled bundle startup replaced by success', value => { value.jobs['release-bundle'].steps.find(step=>step.name==='Verify assembled bundle startup without source or SDKs').run='true'; }],
  ['assembled bundle startup skipped', value => { value.jobs['release-bundle'].steps.find(step=>step.name==='Verify assembled bundle startup without source or SDKs').if='false'; }],
  ['assembled bundle startup failure ignored', value => { value.jobs['release-bundle'].steps.find(step=>step.name==='Verify assembled bundle startup without source or SDKs')['continue-on-error']=true; }],
  ['assembled bundle startup runs before verification', value => { const steps=value.jobs['release-bundle'].steps; const index=steps.findIndex(step=>step.name==='Verify assembled bundle startup without source or SDKs'); const [entry]=steps.splice(index,1); steps.splice(steps.findIndex(step=>step.name==='Verify release bundle completeness and checksums'),0,entry); }],
  ['assembled bundle uploaded before startup check', value => { const steps=value.jobs['release-bundle'].steps; const index=steps.findIndex(step=>step.name==='Verify assembled bundle startup without source or SDKs'); steps.push(...steps.splice(index,1)); }],
  ['verified provenance record not written', value => { const entry=value.jobs['build-images-once'].steps.find(step=>step.name==='Verify exact image provenance before export'); entry.run=entry.run.replace(' --output build-inputs/image-provenance.json',''); }],
  ['verified provenance record not exported', value => { const entry=value.jobs['build-images-once'].steps.find(step=>step.name==='Export exact built images'); entry.run=entry.run.replace('cp build-inputs/image-provenance.json image-artifacts/image-provenance.json','echo omitted'); }],
  ['verified provenance record omitted from checksums', value => { const entry=value.jobs['build-images-once'].steps.find(step=>step.name==='Export exact built images'); entry.run=entry.run.replace('build-metadata.json image-provenance.json > SHA256SUMS','build-metadata.json > SHA256SUMS'); }],
  ...['container-integration','security'].map(job=>[`${job} loaded IDs not compared with built IDs`, value=>{const entry=value.jobs[job].steps.find(step=>step.name==='Load exact built images'); entry.run=entry.run.replace(' --expected image-artifacts/image-provenance.json','');}]),
  ['security omits verified provenance record', value=>{const entry=value.jobs.security.steps.find(step=>step.name==='Verify security input integrity and retain build identity'); entry.run=entry.run.replace('cp image-artifacts/image-provenance.json security-artifacts/image-provenance.json','echo omitted');}],
  ['release omits verified provenance record', value=>{const entry=value.jobs['release-bundle'].steps.find(step=>step.name==='Assemble release bundle'); entry.run=entry.run.replace(' image-artifacts/image-provenance.json bundle/images/',' bundle/images/');}],
  ['real scanner image verification omitted', value => { value.jobs['build-images-once'].steps = value.jobs['build-images-once'].steps.filter(step => step.name !== 'Verify real scanner transport in the exact Worker image'); }],
  ['real scanner image verification replaced by success', value => { value.jobs['build-images-once'].steps.find(step => step.name === 'Verify real scanner transport in the exact Worker image').run = 'true'; }],
  ['real scanner image verification skipped', value => { value.jobs['build-images-once'].steps.find(step => step.name === 'Verify real scanner transport in the exact Worker image').if = 'false'; }],
  ['real scanner image verification failure ignored', value => { value.jobs['build-images-once'].steps.find(step => step.name === 'Verify real scanner transport in the exact Worker image')['continue-on-error'] = true; }],
  ['real scanner uses another Worker image', value => { value.jobs['build-images-once'].steps.find(step => step.name === 'Verify real scanner transport in the exact Worker image').env.STRATAAI_WORKER_IMAGE = 'strataai-worker:latest'; }],
  ['real scanner verification after export', value => { const steps = value.jobs['build-images-once'].steps; const index = steps.findIndex(step => step.name === 'Verify real scanner transport in the exact Worker image'); steps.push(...steps.splice(index, 1)); }],
  ...['Generate SBOMs', 'Block fixed Critical container vulnerabilities'].map(name =>
    [`security loaded provenance after ${name}`, value => { const steps = value.jobs.security.steps; const index = steps.findIndex(step => step.name === 'Load exact built images'); const [loaded] = steps.splice(index, 1); steps.splice(steps.findIndex(step => step.name === name) + 1, 0, loaded); }]),
  ...['container-integration', 'security'].flatMap(job => [
    [`${job} loaded provenance omitted`, value => { const entry = value.jobs[job].steps.find(step => step.name === 'Load exact built images'); entry.run = entry.run.split('\n').filter(line => !line.includes('verify-image-labels.py')).join('\n'); }],
    [`${job} loaded Worker inspection omitted`, value => { const entry = value.jobs[job].steps.find(step => step.name === 'Load exact built images'); entry.run = entry.run.replace('"strataai-worker:${GITHUB_SHA}"', ''); }],
    [`${job} loaded provenance uses different metadata`, value => { const entry = value.jobs[job].steps.find(step => step.name === 'Load exact built images'); entry.run = entry.run.replace('--metadata image-artifacts/build-metadata.json', '--metadata other.json'); }],
    [`${job} loaded provenance failure swallowed`, value => { value.jobs[job].steps.find(step => step.name === 'Load exact built images').run += ' || true'; }],
    [`${job} image loading skipped`, value => { value.jobs[job].steps.find(step => step.name === 'Load exact built images').if = 'false'; }],
    [`${job} image loading permitted to fail`, value => { value.jobs[job].steps.find(step => step.name === 'Load exact built images')['continue-on-error'] = true; }],
    [`${job} loaded provenance performed before last archive`, value => { const entry = value.jobs[job].steps.find(step => step.name === 'Load exact built images'); const lines = entry.run.trim().split('\n'); [lines[3], lines[5]] = [lines[5], lines[3]]; entry.run = lines.join('\n'); }],
  ]),
  ['enabled attachment pipeline replaced by success', value => { step(value, 'Attachment upload and isolated Worker image publication through explicit private test providers').run = 'true'; }],
  ['enabled attachment pipeline errors swallowed', value => { step(value, 'Attachment upload and isolated Worker image publication through explicit private test providers').run += ' || true'; }],
  ['strict two-client label filter recovery omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/label-filter-live.spec.ts', ''); }],
  ['strict Board label workflows omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/card-labels.spec.ts', ''); }],
  ...['Invitation-backed closed registration and atomic expiry against exact release API',
    'Verified-email invitation discovery and retry-safe acceptance',
    'Board administrator demotion continuity and atomic recovery'].map(name =>
    [`full-browser fixture producer omitted: ${name}`, value => { step(value, name).if = "(matrix.suite == 'commands')"; }]),
  ...['Invitation-backed closed registration and atomic expiry against exact release API',
    'Verified-email invitation discovery and retry-safe acceptance',
    'Board administrator demotion continuity and atomic recovery'].map(name =>
    [`private fixture produced after browser consumers: ${name}`, value => {
      const steps = integration(value).steps; const producer = step(value, name);
      steps.splice(steps.indexOf(producer), 1);
      steps.splice(steps.indexOf(step(value, 'Authenticated browser E2E against exact release images')) + 1, 0, producer);
    }]),
  ['strict Board visibility consent scenarios omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/board-visibility.spec.ts', ''); }],
  ['strict Board member consent scenarios omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/board-members.spec.ts', ''); }],
  ['strict Board administrator live scenarios omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/board-admin-live.spec.ts', ''); }],
  ['strict Board directory realtime scenarios omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/organization-board-live.spec.ts', ''); }],
  ['strict Board copy scenarios omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.replace(' tests/browser/board-copy.spec.ts', ''); }],
  ['strict Board fixture verification disabled', value => { delete step(value, 'Strict verified-account Board management and personal preferences').env.STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS; }],
  ['strict Board Worker policy check omitted', value => { const entry = step(value, 'Strict verified-account Board management and personal preferences'); entry.run = entry.run.split('\n').filter(line => !line.includes('exec -T worker printenv')).join('\n'); }],
  ['strict departure account-replacement cases omitted', value => { const entry = step(value, 'Strict verified-account Organization departure and account continuity'); entry.run = entry.run.replace(' tests/browser/organization-departure-account.spec.ts', ''); }],
  ['strict departure fixture verification disabled', value => { delete step(value, 'Strict verified-account Organization departure and account continuity').env.STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS; }],
  ['strict departure Worker policy check omitted', value => { const entry = step(value, 'Strict verified-account Organization departure and account continuity'); entry.run = entry.run.split('\n').filter(line => !line.includes('exec -T worker printenv')).join('\n'); }],
  ['strict personal-watch controls omitted', value => { const entry = step(value, 'Strict verified-account watch producers through native private inboxes'); entry.run = entry.run.replace(' tests/browser/watch-subscriptions.spec.ts', ''); }],
  ['strict personal-watch account fixture disabled', value => { delete step(value, 'Strict verified-account watch producers through native private inboxes').env.STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS; }],
  ['strict watch Worker policy check omitted', value => { const entry = step(value, 'Strict verified-account watch producers through native private inboxes'); entry.run = entry.run.split('\n').filter(line => !line.includes('exec -T worker printenv')).join('\n'); }],
  ['strict inbox consumer omitted', value => { const entry = step(value, 'Strict verified-account assignment mention and reminder native delivery'); entry.run = entry.run.replace(' tests/browser/notification-center.spec.ts', ''); }],
  ['strict inbox consumer filtered out', value => { const entry = step(value, 'Strict verified-account assignment mention and reminder native delivery'); entry.run = entry.run.replace('|recipient inbox recovers', ''); }],
  ['strict account fixture disabled', value => { delete step(value, 'Strict verified-account assignment mention and reminder native delivery').env.STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS; }],
  ['strict Worker policy check omitted', value => { const entry = step(value, 'Strict verified-account assignment mention and reminder native delivery'); entry.run = entry.run.split('\n').filter(line => !line.includes('exec -T worker printenv')).join('\n'); }],
  ['omitted native capacity test', value => { integration(value).steps = integration(value).steps.filter(entry => entry.name !== 'Check concurrent and large-board rank allocation'); }],
  ['removed full browser shard', value => { integration(value).strategy.matrix.include.pop(); }],
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
  ['image source differs from canonical repository', value => { value.jobs['build-images-once'].env.STRATAAI_BUILD_SOURCE = 'https://example.invalid/other'; }],
  ['image creation clock differs from canonical metadata', value => { value.jobs['build-images-once'].env.STRATAAI_BUILD_CREATED = '2026-01-01T00:00:00.000Z'; }],
  ...['web', 'API', 'Worker'].flatMap(host => ['SOURCE', 'CREATED'].map(field =>
    [`${host} image ${field} build argument omitted`, value => { const entry = value.jobs['build-images-once'].steps.find(entry => entry.name === `Build ${host} image`); entry.run = entry.run.replace(`--build-arg STRATAAI_BUILD_${field}="$STRATAAI_BUILD_${field}"`, ''); }])),
  ['image provenance verification omitted', value => { const build = value.jobs['build-images-once']; build.steps = build.steps.filter(entry => entry.name !== 'Verify exact image provenance before export'); }],
  ['image provenance omits Worker inspection', value => { const entry = value.jobs['build-images-once'].steps.find(entry => entry.name === 'Verify exact image provenance before export'); entry.run = entry.run.replace('"strataai-worker:${GITHUB_SHA}"', ''); }],
  ['image provenance refusal swallowed', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Verify exact image provenance before export').run += ' || true'; }],
  ['image provenance permitted to fail', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Verify exact image provenance before export')['continue-on-error'] = true; }],
  ['image provenance verification skipped', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Verify exact image provenance before export').if = 'false'; }],
  ['image provenance performed after export', value => { const build = value.jobs['build-images-once']; const index = build.steps.findIndex(entry => entry.name === 'Verify exact image provenance before export'); const [entry] = build.steps.splice(index, 1); build.steps.push(entry); }],
  ['image provenance regression tests omitted', value => { const entry = value.jobs['web-quality'].steps.find(entry => entry.name === 'Verify source test report completeness and privacy'); entry.run = entry.run.replace("python3 -m unittest discover -s tests -p 'image_labels_test.py'", ''); }],
  ['image provenance source tests skipped', value => { value.jobs['web-quality'].steps.find(entry => entry.name === 'Verify source test report completeness and privacy').if = 'false'; }],
  ['image provenance source tests permitted to fail', value => { value.jobs['web-quality'].steps.find(entry => entry.name === 'Verify source test report completeness and privacy')['continue-on-error'] = true; }],
  ['image arguments recalculate build version', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Build web image').run = 'docker build --build-arg STRATAAI_BUILD_VERSION=other .'; }],
  ['image archive metadata lacks checksums', value => { value.jobs['build-images-once'].steps.find(entry => entry.name === 'Export exact built images').run = 'echo unchecked'; }],
  ['release metadata reconstructed downstream', value => { value.jobs['release-bundle'].steps.find(entry => entry.name === 'Assemble release bundle').run = 'cat > bundle/build-metadata.json'; }],
  ['security archive checksums bypassed', value => { value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity').run = 'cp image-artifacts/build-metadata.json security-artifacts/build-metadata.json'; }],
  ['security verification moved after loading', value => { const steps = value.jobs.security.steps; const index = steps.findIndex(entry => entry.name === 'Verify security input integrity and retain build identity'); steps.push(...steps.splice(index, 1)); }],
  ['security identity silently omitted', value => { const entry = value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity'); entry.run = entry.run.replace('cp image-artifacts/build-metadata.json security-artifacts/build-metadata.json', 'echo omitted'); }],
  ['security verification failure ignored', value => { value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity')['continue-on-error'] = true; }],
  ['security version diverges from metadata', value => { value.jobs.security.steps.find(entry => entry.name === 'Verify security input integrity and retain build identity').env.STRATAAI_BUILD_VERSION = 'other'; }],
  ['capacity evidence uploads unstaged payload', value => { step(value, 'Retain fixed-scope Checklist capacity measurements').with.path = 'artifacts/capacity/checklists.json'; }],
  ['evidence staging broadens to private files', value => { step(value, 'Prepare Retain fixed operator collection evidence').run += ' --entry "private.env" "private.env"'; }],
  ['evidence identity comes from another source', value => { const entry = step(value, 'Prepare Retain actual large-Board native capacity diagnostics'); entry.run = entry.run.replace('image-artifacts/build-metadata.json', 'other/build-metadata.json'); }],
  ['failure evidence staging skipped', value => { step(value, 'Prepare Retain browser failure evidence').if = "(matrix.suite == 'browser-foundation' || matrix.suite == 'browser-notifications' || matrix.suite == 'browser-full')"; }],
  ['release omits tested security evidence', value => { value.jobs['release-bundle'].steps = value.jobs['release-bundle'].steps.filter(entry => entry.name !== 'Download exact tested security evidence'); }],
  ['release input corruption goes unchecked', value => { value.jobs['release-bundle'].steps.find(entry => entry.name === 'Verify tested release inputs').run = 'echo unchecked'; }],
  ['release bundle validation failure ignored', value => { value.jobs['release-bundle'].steps.find(entry => entry.name === 'Verify release bundle completeness and checksums')['continue-on-error'] = true; }],
  ['environment example excluded by uploader', value => { delete value.jobs['release-bundle'].steps.find(entry => entry.name === 'Upload runnable release bundle').with['include-hidden-files']; }],
  ['release SBOM directory remains empty', value => { const entry = value.jobs['release-bundle'].steps.find(entry => entry.name === 'Assemble release bundle'); entry.run = entry.run.replace('cp -R security-artifacts bundle/security', 'echo omitted'); }],
  ['security checksum generation bypassed', value => { value.jobs.security.steps.find(entry => entry.name === 'Bind retained security evidence checksums').run = 'echo unchecked'; }],
  ['original image evidence lost during release copying', value => { const entry = value.jobs['release-bundle'].steps.find(entry => entry.name === 'Assemble release bundle'); entry.run = entry.run.replace('cp image-artifacts/SHA256SUMS image-artifacts/build-metadata.json image-artifacts/image-provenance.json bundle/images/', 'echo omitted'); }],
  ['final bundle no longer checked against original inputs', value => { value.jobs['release-bundle'].steps.find(entry => entry.name === 'Verify release bundle completeness and checksums').run = 'python3 scripts/ci/verify-release-artifacts.py bundle --path bundle'; }],
  ['browser shard repeated', value => { integration(value).strategy.matrix.include.at(-1).shard = 1; }],
  ['browser total inconsistent', value => { integration(value).strategy.matrix.include.at(-1).totalShards = 5; }],
  ['browser coverage check omitted', value => { value.jobs['web-quality'].steps = value.jobs['web-quality'].steps.filter(entry => entry.name !== 'Verify complete browser shard coverage'); }],
  ['full browser command adds filtering', value => { step(value, 'Authenticated browser E2E against exact release images').run += ' --grep fast'; }],
  ['browser diagnostics collide between shards', value => { step(value, 'Retain browser failure evidence').with.name = 'shared-browser-diagnostics'; }],
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

const attachmentSources = () => ({
  script: readFileSync('scripts/ci/test-attachment-image-pipeline.sh', 'utf8'),
  background: readFileSync('playwright.attachment.config.ts', 'utf8'),
  lifecycle: readFileSync('playwright.attachment-lifecycle.config.ts', 'utf8'),
});
for (const [name, mutate] of [
  ['background phase omitted', value => { value.script = value.script.replace('  npx playwright test --config playwright.attachment.config.ts', '  true'); }],
  ['lifecycle and upload phase omitted', value => { value.script = value.script.replace('  npx playwright test --config playwright.attachment-lifecycle.config.ts', '  true'); }],
  ['background phase filtered', value => { value.script = value.script.replace('npx playwright test --config playwright.attachment.config.ts', 'npx playwright test --config playwright.attachment.config.ts --grep consent'); }],
  ['lifecycle and upload phase filtered', value => { value.script = value.script.replace('npx playwright test --config playwright.attachment-lifecycle.config.ts', 'npx playwright test --config playwright.attachment-lifecycle.config.ts --grep archive'); }],
  ['lifecycle errors swallowed', value => { value.script = value.script.replace('npx playwright test --config playwright.attachment-lifecycle.config.ts', 'npx playwright test --config playwright.attachment-lifecycle.config.ts || true'); }],
  ['shell fail-fast disabled', value => { value.script = value.script.replace('set -Eeuo pipefail', 'set -Eeuo pipefail\nset +e'); }],
  ['background release headers disabled', value => { value.script = value.script.replace('STRATAAI_E2E_RELEASE_HEADERS=1', 'STRATAAI_E2E_RELEASE_HEADERS=0'); }],
  ['lifecycle pacing disabled', value => { value.script = value.script.replace('STRATAAI_ATTACHMENT_BROWSER_FIXTURE="$scratch/archive-browser-fixture" STRATAAI_E2E_RATE_PACING=1', 'STRATAAI_ATTACHMENT_BROWSER_FIXTURE="$scratch/archive-browser-fixture" STRATAAI_E2E_RATE_PACING=0'); }],
  ['lifecycle owned fixture replaced', value => { value.script = value.script.replace('STRATAAI_ATTACHMENT_BROWSER_FIXTURE="$scratch/archive-browser-fixture"', 'STRATAAI_ATTACHMENT_BROWSER_FIXTURE="$scratch/browser-fixture"'); }],
  ['background case replaced', value => { value.background = value.background.replace('board-background-pipeline.case.ts', 'board.spec.ts'); }],
  ['lifecycle case replaced', value => { value.lifecycle = value.lifecycle.replace('attachment-source-archive-pipeline.case.ts', 'attachment-lifecycle.spec.ts'); }],
  ['lifecycle config hides failed attempts', value => { value.lifecycle = value.lifecycle.replace('...release,', '...release, retries: 2,'); }],
]) {
  test(`rejects attachment coverage regression: ${name}`, () => {
    const value = attachmentSources(); mutate(value);
    assert.throws(() => verifyIntegrationSuites(workflow(), registry, value));
  });
}
