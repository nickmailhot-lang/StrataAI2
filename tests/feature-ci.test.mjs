import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { execFileSync, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { parseDocument } from 'yaml';
import { selectImpact, policy } from '../scripts/ci/select-feature-tests.mjs';
import { requireFeatureChecks } from '../scripts/ci/require-feature-checks.mjs';
const inventory = readdirSync('tests/browser').filter(file => file.endsWith('.spec.ts')).map(file => `tests/browser/${file}`);
const workflow = name => {
  const document = parseDocument(readFileSync(`.github/workflows/${name}.yml`, 'utf8'), { uniqueKeys: true });
  assert.deepEqual(document.errors, []);
  return document.toJS();
};
test('documentation changes retain contracts and omit runtime', () => {
  const plan = selectImpact(['docs/architecture/example.md'], inventory);
  assert.equal(plan.full, false); assert.equal(plan.web, false); assert.equal(plan.runtime, false);
  assert.ok(plan.omitted.length);
});
test('leaf labels select real label/filter cases plus critical persisted workflow', () => {
  const plan = selectImpact(['apps/web/src/features/kanban/CardLabelsControl.tsx'], inventory);
  assert.equal(plan.full, false); assert.equal(plan.web, true); assert.equal(plan.runtime, true);
  assert.ok(plan.browser.includes('tests/browser/card-labels.spec.ts'));
  assert.ok(plan.browser.includes('tests/browser/label-filter-live.spec.ts'));
  assert.ok(plan.browser.includes('tests/browser/board.spec.ts'));
  assert.ok(plan.browser.length < inventory.length);
});
for (const path of ['src/StrataAI.Api/Program.cs','db/migrations/001.sql','Dockerfile.web','package-lock.json',
  'Directory.Packages.props','.github/workflows/feature-ci.yml','scripts/ci/feature-impact.json',
  'apps/web/src/app/InternalAppShell.tsx','apps/web/src/features/auth/AuthPage.tsx',
  'apps/web/src/features/kanban/new-unmapped.ts','tests/browser/scopedBoardWorker.ts',
  'apps/web/src/features/kanban/CardPermissions.ts']) {
  test(`shared, unknown and security changes expand coverage: ${path}`, () => {
    const plan = selectImpact([path], inventory);
    assert.equal(plan.full, true); assert.equal(plan.runtime, false);
  });
}
test('real provider features escalate to complete immutable fixtures', () => {
  for (const path of ['CardReminderControl.tsx', 'AttachmentManageControl.tsx', 'CardAssigneeControl.tsx'])
    assert.equal(selectImpact([`apps/web/src/features/kanban/${path}`], inventory).full, true);
});
test('new, deleted and explicitly forced test selections fail closed', () => {
  assert.equal(selectImpact([], inventory).full, true);
  assert.equal(selectImpact(['unknown.xyz'], inventory).full, true);
  assert.equal(selectImpact(['tests/browser/removed.spec.ts'], inventory).full, true);
  assert.equal(selectImpact(['docs/readme.md'], inventory, { forceFull: true }).full, true);
  assert.throws(() => selectImpact(['../secrets'], inventory));
});
test('explicit browser file is retained intact alongside baseline', () => {
  const file = inventory.find(file => /label-filter-live/.test(file));
  const plan = selectImpact([file], inventory);
  assert.ok(plan.browser.includes(file)); assert.ok(policy.baselineBrowser.every(file => plan.browser.includes(file)));
});
const valid = { SELECTION_RESULT: 'success', WEB_SELECTED: 'true', RUNTIME_SELECTED: 'true', FULL_SELECTED: 'false', WEB_RESULT: 'success', RUNTIME_RESULT: 'success', FULL_RESULT: 'skipped' };
test('feature aggregate accepts exactly required successful checks', () => {
  requireFeatureChecks(valid);
  requireFeatureChecks({ ...valid, WEB_SELECTED: 'false', RUNTIME_SELECTED: 'false', WEB_RESULT: 'skipped', RUNTIME_RESULT: 'skipped' });
  requireFeatureChecks({ ...valid, FULL_SELECTED: 'true', RUNTIME_SELECTED: 'false', WEB_RESULT: 'skipped', RUNTIME_RESULT: 'skipped', FULL_RESULT: 'success' });
});
for (const field of ['SELECTION_RESULT','WEB_RESULT','RUNTIME_RESULT']) for (const result of ['failure','cancelled','skipped','']) {
  test(`aggregate refuses ${field}=${result}`, () => assert.throws(() => requireFeatureChecks({ ...valid, [field]: result })));
}
test('aggregate rejects missing selectors, unexpected executed jobs and duplicate runtime', () => {
  assert.throws(() => requireFeatureChecks({ ...valid, FULL_SELECTED: '' }));
  assert.throws(() => requireFeatureChecks({ ...valid, FULL_RESULT: 'success' }));
  assert.throws(() => requireFeatureChecks({ ...valid, FULL_SELECTED: 'true' }));
});
test('full suite remains separate; feature and main workflows expose stable gates', () => {
  const full = workflow('full-sanity'), feature = workflow('feature-ci'), smoke = workflow('main-smoke');
  assert.ok(full.on.schedule.length); assert.ok(full.on.push.tags.includes('v*'));
  assert.ok('workflow_call' in full.on && 'workflow_dispatch' in full.on);
  assert.equal(full.on.pull_request, undefined); assert.equal(full.on.push.branches, undefined);
  assert.equal(full.jobs['required-ci'].name, 'full-sanity-gate');
  assert.deepEqual(feature.jobs['required-ci'].needs, ['select','web','runtime','full']);
  assert.equal(feature.jobs['required-ci'].if, 'always()');
  assert.ok(feature.on.merge_group !== undefined || Object.hasOwn(feature.on, 'merge_group'));
  assert.equal(feature.jobs.full.uses, './.github/workflows/full-sanity.yml');
  assert.equal(smoke.jobs['demo-ready'].if, 'always()');
  assert.deepEqual(smoke.jobs['demo-ready'].needs, 'smoke');
  assert.equal(feature.concurrency['cancel-in-progress'], true);
  assert.equal(smoke.concurrency['cancel-in-progress'], false);
});
test('generated runtime retains baseline security, actual release fixtures and image provenance', () => {
  execFileSync(process.execPath, ['scripts/ci/generate-feature-runtime.mjs', '--check']);
  const runtime = workflow('feature-runtime');
  const steps = runtime.jobs.runtime.steps;
  for (const name of [...policy.baselineCommands, 'Reject unsafe runtime database roles in exact images',
    'Apply release-database migrations','Prepare isolated identity mail transport and ephemeral signing keys',
    'Enable automatic Organization metadata and invitation authority routing for native browser acceptance']) {
    const step = steps.find(step => step.name === name); assert.ok(step, name); assert.equal(step.if, undefined);
  }
  assert.equal(runtime.jobs['release-bundle'], undefined);
  assert.equal(runtime.jobs.runtime.strategy, undefined);
  assert.doesNotMatch(JSON.stringify(runtime), /\$\{\{ matrix\./);
  const run = steps.find(step => step.name === 'Selected feature browser acceptance against exact images');
  assert.equal(run.env.STRATAAI_E2E_RATE_PACING, '1'); assert.equal(run.env.STRATAAI_E2E_RELEASE_HEADERS, '1');
});
test('browser executor refuses empty, omitted baseline and command-injection selections before spawning', () => {
  for (const files of [[], ['tests/browser/card-labels.spec.ts'], [...policy.baselineBrowser, '$(touch /tmp/unsafe)']]) {
    const result = spawnSync(process.execPath, ['scripts/ci/run-feature-browser.mjs'], { encoding: 'utf8', env: { ...process.env, STRATAAI_SELECTED_BROWSER: JSON.stringify(files) } });
    assert.notEqual(result.status, 0); assert.match(result.stderr, /Invalid browser selection/);
  }
});
test('Git diff retains both sides of renames and deleted paths without truncation', () => {
  const folder = mkdtempSync(join(tmpdir(), 'strataai-impact-'));
  const git = (...args) => execFileSync('git', args, { cwd: folder, encoding: 'utf8' }).trim();
  try {
    git('init'); git('config','user.email','ci@example.test'); git('config','user.name','CI fixture');
    writeFileSync(join(folder,'old.ts'),'same'); git('add','.'); git('commit','-m','base'); const base=git('rev-parse','HEAD');
    git('mv','old.ts','new.ts'); git('commit','-m','rename');
    const result=execFileSync('git',['diff','--no-renames','--name-only','-z',`${base}...HEAD`],{cwd:folder,encoding:'utf8'}).split('\0').filter(Boolean);
    assert.deepEqual(result.sort(),['new.ts','old.ts']);
  } finally { rmSync(folder,{recursive:true,force:true}); }
});

test('qualification record rejects evidence from another revision or workflow run', async () => {
  const { qualificationRecord } = await import('../scripts/ci/qualification-record.mjs');
  const metadata = { commitSha: 'a'.repeat(40), repository: 'owner/repo', workflowRunId: '123', workflowRunAttempt: '1', version: '1.0.0' };
  const env = { GITHUB_SHA: metadata.commitSha, GITHUB_RUN_ID: '123' };
  const images = { web: 'sha256:' + 'b'.repeat(64) };
  assert.equal(qualificationRecord(metadata, images, env, 'c'.repeat(64)).qualification, 'full-sanity');
  assert.throws(() => qualificationRecord(metadata, images, { ...env, GITHUB_SHA: 'd'.repeat(40) }, 'c'.repeat(64)));
  assert.throws(() => qualificationRecord(metadata, images, { ...env, GITHUB_RUN_ID: '124' }, 'c'.repeat(64)));
  assert.throws(() => qualificationRecord(metadata, images, env, 'invalid'));
  const full = workflow('full-sanity');
  const steps = full.jobs['release-bundle'].steps;
  assert.ok(steps.findIndex(step => step.name === 'Record exact revision and immutable images qualified by full sanity') > steps.findIndex(step => step.name === 'Verify bundle unchanged after startup smoke'));
  assert.equal(steps.find(step => step.name === 'Record exact revision and immutable images qualified by full sanity').if, undefined);
});

test('root test command forwards CLI flags through the npm workspace to the installed Vitest runner', () => {
  const version = JSON.parse(readFileSync('node_modules/vitest/package.json', 'utf8')).version;
  const result = spawnSync('npm', ['test', '--', '--help'], { encoding: 'utf8', timeout: 15_000 });
  assert.equal(result.status, 0, result.stderr);
  assert.ok(result.stdout.includes(`vitest/${version}`), 'Flags must reach Vitest rather than being consumed by npm run');
});

test('all full-qualification .NET consumers install the verified SDK without floating channel resolution', () => {
  const full = workflow('full-sanity');
  for (const job of ['dotnet-quality', 'postgres-integration', 'security']) {
    const setup = full.jobs[job].steps.filter(step => step.uses?.startsWith('actions/setup-dotnet@'));
    assert.equal(setup.length, 1, `${job} must retain one SDK setup`);
    assert.equal(setup[0].with['dotnet-version'], '10.0.402', `${job} must not resolve an unavailable floating release`);
    assert.equal(setup[0].with.cache, true);
    assert.equal(setup[0].with['cache-dependency-path'], '**/packages.lock.json');
  }
});
