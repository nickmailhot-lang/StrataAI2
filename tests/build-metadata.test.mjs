import assert from 'node:assert/strict';
import test from 'node:test';
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createBuildMetadata, metadataOutputs } from '../scripts/ci/build-metadata.mjs';

const sha = 'a'.repeat(40), clock = '2026-10-08T21:00:00.000Z';
const env = { GITHUB_SHA: sha, GITHUB_REPOSITORY: 'example/StrataAI2', GITHUB_RUN_ID: '123',
  GITHUB_RUN_NUMBER: '456', GITHUB_RUN_ATTEMPT: '2', GITHUB_REF: 'refs/heads/main' };
const create = (changes = {}, version = '0.1.0', checkout = sha, at = clock) =>
  createBuildMetadata({ ...env, ...changes }, checkout, version, at);

test('main retains the existing run version and exact image identity', () => {
  assert.deepEqual(create(), { repository: 'example/StrataAI2', commitSha: sha, shortSha: 'a'.repeat(12),
    workflowRunId: '123', workflowRunNumber: '456', workflowRunAttempt: '2', version: '0.1.0-456',
    releaseVersion: null, imageTag: sha, createdAt: clock,
    images: { web: `strataai-web:${sha}`, api: `strataai-api:${sha}`, worker: `strataai-worker:${sha}` } });
});
test('PR metadata uses the triggering merge SHA, not a branch tip', () => {
  const merge = 'b'.repeat(40);
  assert.equal(create({ GITHUB_SHA: merge, GITHUB_REF: 'refs/pull/123/merge' }, '0.1.0', merge).commitSha, merge);
  assert.throws(() => create({ GITHUB_SHA: merge, GITHUB_REF: 'refs/pull/123/merge' }));
});
test('tag metadata uses the release version while image tags remain the SHA', () => {
  for (const version of ['1.2.3', '1.2.3-rc.1', '1.2.3-rc.1+build.001']) {
    const value = create({ GITHUB_REF: `refs/tags/v${version}` });
    assert.equal(value.version, version); assert.equal(value.releaseVersion, version); assert.equal(value.imageTag, sha);
  }
});
test('prerelease package builds preserve their identifiers and append the run', () => {
  assert.equal(create({}, '0.1.0-rc.1+test.001').version, '0.1.0-rc.1.456+test.001');
});
test('run identities preserve values above JavaScript safe integer precision', () => {
  assert.equal(create({ GITHUB_RUN_ID: '9007199254740993' }).workflowRunId, '9007199254740993');
});
test('supported SHA-256 repository identity is not truncated for image tagging', () => {
  const long = 'c'.repeat(64); assert.equal(create({ GITHUB_SHA: long }, '0.1.0', long).imageTag, long);
});
test('job output records contain only the known validated fields', () => {
  const records = metadataOutputs(create()).trim().split('\n');
  assert.equal(records.length, 7);
  assert.equal(records.find(row => row.startsWith('version=')), 'version=0.1.0-456');
  assert.ok(records.every(row => /^[a-z_]+=[^\r\n]+$/.test(row)));
});
for (const [key, value] of [['GITHUB_SHA', 'a'.repeat(39)], ['GITHUB_SHA', sha.toUpperCase()],
  ['GITHUB_REPOSITORY', 'example/repo\ninjected=value'], ['GITHUB_RUN_ID', '0'], ['GITHUB_RUN_NUMBER', '01'],
  ['GITHUB_RUN_ATTEMPT', '-1'], ['GITHUB_REF', 'refs/heads/main\ninjected=value']]) {
  test(`rejects malformed ${key}`, () => assert.throws(() => create({ [key]: value })));
}
for (const version of ['01.2.3', '1.02.3', '1.2.03', '1.2.3-01', '1.2.3-rc..1', '1.2.3+', 'garbage', '1.2.3\ninjected=value']) {
  test(`rejects malformed release version ${JSON.stringify(version)}`, () => assert.throws(() => create({ GITHUB_REF: `refs/tags/v${version}` })));
}
test('refuses a tag without its release prefix', () => assert.throws(() => create({ GITHUB_REF: 'refs/tags/1.2.3' })));
test('refuses trailing newlines in every workflow output source', () => {
  for (const key of Object.keys(env)) assert.throws(() => create({ [key]: `${env[key]}\n` }));
});
test('refuses an invalid source package version', () => assert.throws(() => create({}, 'invalid')));
test('refuses a normalized impossible creation calendar', () => assert.throws(() => create({}, '0.1.0', sha, '2026-02-30T00:00:00.000Z')));
test('CLI refuses a different checkout before publication, then emits the actual checked-out commit', () => {
  const directory = mkdtempSync(join(tmpdir(), 'strataai-metadata-test-'));
  try {
    const git = args => execFileSync('git', args, { cwd: directory, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
    git(['init', '--quiet']);
    writeFileSync(join(directory, 'package.json'), '{"version":"0.1.0"}');
    git(['add', 'package.json']);
    git(['-c', 'user.name=Metadata test', '-c', 'user.email=metadata@example.test', '-c', 'commit.gpgsign=false', 'commit', '--quiet', '-m', 'synthetic metadata fixture']);
    const actual = git(['rev-parse', 'HEAD']);
    const output = join(directory, 'job-output');
    const run = trigger => spawnSync(process.execPath, [fileURLToPath(new URL('../scripts/ci/build-metadata.mjs', import.meta.url))],
      { cwd: directory, encoding: 'utf8', env: { ...process.env, ...env, GITHUB_SHA: trigger, GITHUB_OUTPUT: output } });
    const refused = run(actual === sha ? 'b'.repeat(40) : sha);
    assert.equal(refused.status, 1);
    assert.equal(refused.stderr.trim(), 'Build metadata validation or publication failed.');
    assert.equal(existsSync(output), false);
    assert.equal(existsSync(join(directory, 'build-metadata.json')), false);
    const accepted = run(actual);
    assert.equal(accepted.status, 0, 'Actual checkout metadata invocation must succeed');
    const emitted = JSON.parse(readFileSync(join(directory, 'build-metadata.json'), 'utf8'));
    assert.equal(emitted.commitSha, actual); assert.equal(emitted.version, '0.1.0-456');
    assert.equal(emitted.workflowRunId, '123'); assert.equal(emitted.imageTag, actual);
    assert.equal(readFileSync(output, 'utf8'), metadataOutputs(emitted));
  } finally {
    // This path is returned directly by mkdtemp; no user files are traversed.
    rmSync(directory, { recursive: true, force: true });
  }
});
