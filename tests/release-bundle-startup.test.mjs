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
