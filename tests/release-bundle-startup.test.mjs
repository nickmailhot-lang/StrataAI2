import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
const script = resolve('scripts/ci/test-release-bundle-startup.sh');
function fixture(check) {
  const root = mkdtempSync(join(tmpdir(), 'strataai-startup-refusal-'));
  try {
    writeFileSync(join(root, 'build-metadata.json'), '{}');
    check(root, () => spawnSync(bash, [script, root], { encoding: 'utf8',
      env: { ...process.env, PATH: root, BASH_ENV: undefined } }));
  } finally {
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
    assert.match(basename(root), /^strataai-startup-refusal-/);
    rmSync(root, { recursive: true, force: true });
  }
}

test('startup refuses existing operator settings without rewriting or requiring any external command', () => {
  fixture((root, run) => {
    const original = 'PUBLIC_TEST_FIXTURE=operator-settings-must-survive\n';
    writeFileSync(join(root, '.env'), original);
    const result = run();
    assert.equal(result.status, 1);
    assert.equal(result.stdout, '');
    assert.equal(result.stderr, '');
    assert.equal(readFileSync(join(root, '.env'), 'utf8'), original);
  });
});

test('startup refuses application checkouts before secret creation or Docker/SDK commands', () => {
  for (const source of ['.git', 'src', 'apps']) fixture((root, run) => {
    mkdirSync(join(root, source));
    const result = run();
    assert.equal(result.status, 1);
    assert.equal(result.stdout, '');
    assert.equal(result.stderr, '');
    assert.throws(() => readFileSync(join(root, '.env')), { code: 'ENOENT' });
  });
});

// Execute the maintained EXIT handler with a Docker stand-in. This exercises
// failure handling without creating containers or touching operator resources.
const cleanup = readFileSync(script, 'utf8').match(/cleanup\(\) \{[\s\S]*?\n\}/)?.[0];
assert.ok(cleanup, 'Maintained release cleanup function must be present');
for (const scenario of [
  { name: 'clean namespace', status: 0 },
  { name: 'upstream startup failure', upstream: 37, status: 37 },
  ...['container', 'volume', 'network'].map(leftover => ({ name: `leftover ${leftover}`, leftover, status: 1 })),
  ...['container', 'volume', 'network'].map(queryFailure => ({ name: `${queryFailure} query failure`, queryFailure, status: 1 })),
  { name: 'Compose teardown failure', downFailure: true, status: 1 },
]) test(`release cleanup fails closed: ${scenario.name}`, () => {
  fixture(root => {
    writeFileSync(join(root, '.env'), 'PRIVATE_TEST_SETTINGS=discard\n');
    writeFileSync(join(root, 'inspection.json'), 'PRIVATE_TEST_INSPECTION\n');
    const driver = join(root, 'cleanup-test.sh');
    writeFileSync(driver, `#!/usr/bin/env bash
set -euo pipefail
project=strataai-bundle-smoke-0123456789abcdef
compose=(docker compose)
inspection_file=inspection.json
docker() {
  printf '%s\\n' "$*" >> calls.log
  if [ "$1" = compose ]; then
    if [ "$STRATAAI_TEST_DOWN_FAILURE" = 1 ]; then return 9; fi
    return 0
  fi
  local task_kind=$1
  if [ "$task_kind" = ps ]; then task_kind=container; fi
  if [ "$task_kind" = "$STRATAAI_TEST_QUERY_FAILURE" ]; then return 9; fi
  if [ "$task_kind" = "$STRATAAI_TEST_LEFTOVER" ]; then printf '%s\\n' owned-resource; fi
  return 0
}
${cleanup}
trap cleanup EXIT
exit "$STRATAAI_TEST_UPSTREAM"
`);
    const result = spawnSync(bash, [driver], { cwd: root, encoding: 'utf8', env: { ...process.env, BASH_ENV: undefined,
      STRATAAI_TEST_DOWN_FAILURE: scenario.downFailure ? '1' : '0',
      STRATAAI_TEST_QUERY_FAILURE: scenario.queryFailure ?? '', STRATAAI_TEST_LEFTOVER: scenario.leftover ?? '',
      STRATAAI_TEST_UPSTREAM: String(scenario.upstream ?? 0) } });
    assert.equal(result.status, scenario.status, result.stderr);
    assert.equal(result.stdout, '');
    assert.throws(() => readFileSync(join(root, '.env')), { code: 'ENOENT' });
    assert.throws(() => readFileSync(join(root, 'inspection.json')), { code: 'ENOENT' });
    const calls = readFileSync(join(root, 'calls.log'), 'utf8').trim().split('\n');
    assert.deepEqual(calls, [
      'compose down --volumes --remove-orphans',
      'ps -aq --filter label=com.docker.compose.project=strataai-bundle-smoke-0123456789abcdef',
      'volume ls -q --filter label=com.docker.compose.project=strataai-bundle-smoke-0123456789abcdef',
      'network ls -q --filter label=com.docker.compose.project=strataai-bundle-smoke-0123456789abcdef',
    ]);
  });
});
