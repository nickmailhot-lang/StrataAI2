import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const analyzer = resolve('scripts/analyze-ticket-dependencies.mjs');
function issue(id, number, criteria, dependencies = 'None', state = 'open') {
  return { title: `[${id}] Fixture`, number, state, state_reason: null,
    html_url: `https://github.com/example/fixture/issues/${number}`, updated_at: '2026-10-15T00:00:00Z',
    body: `## Requirements\n${id}-FR-001\n${id}-FR-001\n## Acceptance Criteria\n${criteria}\n## Dependencies\n${dependencies}\n` };
}
function analyze(issues, check) {
  const folder = mkdtempSync(join(tmpdir(), 'strataai-ticket-analysis-'));
  try {
    const input = join(folder, 'issues.json'), output = join(folder, 'map.md');
    writeFileSync(input, JSON.stringify(issues));
    const result = spawnSync(process.execPath, [analyzer, input, output], { encoding: 'utf8', timeout: 10000 });
    check(result, () => readFileSync(output, 'utf8'));
  } finally {
    assert.equal(dirname(realpathSync(folder)), realpathSync(tmpdir()));
    assert.match(basename(folder), /^strataai-ticket-analysis-/);
    rmSync(folder, { recursive: true, force: true });
  }
}
function row(map, id) {
  const line = map.split('\n').find(value => value.startsWith(`| [${id}:`));
  assert.ok(line, `Missing canonical row: ${id}`);
  return line.split('|').map(value => value.trim());
}

test('mixed PRD and architecture criteria are counted without losing either identifier convention', () => {
  analyze([
    issue('PRD-02', 2, '**AC-AUTH-02-01**\n**AC-AUTH-02-02**\n**AC-AUTH-02-03**\nAC-AUTH-02-01'),
    issue('ARCH-08', 89, Array.from({ length: 4 }, (_, n) => `- **ARCH-08-AC-${String(n + 1).padStart(3, '0')}** — GIVEN a fixture THEN verify it.`).join('\n'), 'PRD-02'),
  ], (result, read) => {
    assert.equal(result.status, 0, result.stderr);
    const map = read();
    assert.equal(row(map, 'PRD-02')[4], '1'); assert.equal(row(map, 'PRD-02')[5], '3');
    assert.equal(row(map, 'ARCH-08')[4], '1'); assert.equal(row(map, 'ARCH-08')[5], '4');
    assert.match(map, /2 PRD\/architecture tickets; 2 open and 0 closed/);
  });
});

test('all twenty immutable-CI criteria survive repeated cross-references and three-digit suffixes', () => {
  const criteria = Array.from({ length: 20 }, (_, n) => `ARCH-11-AC-${String(n + 1).padStart(3, '0')}`).join('\n');
  analyze([issue('ARCH-11', 92, criteria + '\nARCH-11-AC-001\nARCH-11-AC-020')], (result, read) => {
    assert.equal(result.status, 0, result.stderr);
    assert.equal(row(read(), 'ARCH-11')[5], '20');
    assert.match(read(), /does not prove implementation or acceptance/);
  });
});

test('closed canonical definitions remain in dependency order while closed duplicates remain excluded', () => {
  const canonical = issue('PRD-03', 3, '**AC-ORG-03-01**', 'None', 'closed');
  const duplicate = { ...canonical, number: 4, html_url: 'https://github.com/example/fixture/issues/4', state_reason: 'duplicate' };
  analyze([issue('PRD-02', 2, '**AC-AUTH-02-01**', 'PRD-03'), canonical, duplicate], (result, read) => {
    assert.equal(result.status, 0, result.stderr);
    assert.match(read(), /2 PRD\/architecture tickets; 1 open and 1 closed/);
    assert.equal(row(read(), 'PRD-03')[2], 'closed');
    assert.match(read(), /GitHub reports these closed duplicates/);
    const groups = read().split('## Dependency groups')[1].split('## Ticket definitions')[0];
    assert.ok(groups.indexOf('[PRD-03]') < groups.indexOf('[PRD-02]'));
  });
});

for (const [name, body, message] of [
  ['missing dependency definition', '## Requirements\nPRD-02-FR-001', /Missing dependency section/],
  ['absent referenced ticket', '\n## Dependencies\nPRD-01\n', /Dependency PRD-01 .* is absent/],
]) test(`incomplete inventory refuses ${name} without publishing a map`, () => {
  analyze([{ ...issue('PRD-02', 2, '**AC-AUTH-02-01**'), body }], (result, read) => {
    assert.notEqual(result.status, 0); assert.match(result.stderr, message);
    assert.throws(read, { code: 'ENOENT' });
  });
});
