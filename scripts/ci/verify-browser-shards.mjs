import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const shardCount = 4;

export function cases(report) {
  assert.deepEqual(report.errors, [], 'Browser collection must succeed');
  assert.equal(report.config.workers, 1, 'Preserve sequential execution within each topology');
  assert.equal(report.config.fullyParallel, false, 'Preserve complete file ownership');
  assert.ok(report.config.projects.every(project => project.retries === 0), 'No browser retries');
  const result = [];
  const visit = suite => {
    for (const spec of suite.specs ?? []) for (const test of spec.tests) {
      result.push({ id: `${spec.id}\0${test.projectId}`, file: `${spec.file}\0${test.projectId}`,
        timeout: test.timeout, expectedStatus: test.expectedStatus });
    }
    for (const child of suite.suites ?? []) visit(child);
  };
  for (const suite of report.suites) visit(suite);
  assert.ok(result.length > 0, 'Every partition must contain cases');
  assert.equal(new Set(result.map(row => row.id)).size, result.length, 'Duplicate collected case');
  return result;
}

export function verifyPartition(full, partitions) {
  assert.equal(partitions.length, shardCount);
  const expected = new Map(full.map(row => [row.id, row]));
  assert.equal(expected.size, full.length);
  assert.ok(full.length > 0);
  const seen = new Set();
  const files = new Map();
  for (const [index, rows] of partitions.entries()) {
    assert.ok(rows.length > 0, 'Empty shard');
    for (const row of rows) {
      assert.ok(!seen.has(row.id), 'Case repeated across shards');
      assert.deepEqual(row, expected.get(row.id), 'Unchanged case identity, file, timeout and expected outcome');
      assert.ok(!files.has(row.file) || files.get(row.file) === index, 'A file may belong to only one shard');
      files.set(row.file, index); seen.add(row.id);
    }
  }
  assert.deepEqual([...seen].sort(), [...expected.keys()].sort(), 'Every complete-suite case must run once');
  return { cases: full.length, files: files.size, counts: partitions.map(rows => rows.length) };
}

export function collectAndVerify() {
  const parent = realpathSync(tmpdir());
  const folder = mkdtempSync(join(parent, 'strataai-browser-shards-'));
  try {
    const collect = index => {
      const output = join(folder, `collection-${index}.json`);
      const args = ['node_modules/@playwright/test/cli.js', 'test', '--list', '--reporter=json'];
      if (index) args.push(`--shard=${index}/${shardCount}`);
      const result = spawnSync(process.execPath, args, { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024,
        env: { ...process.env, PLAYWRIGHT_JSON_OUTPUT_FILE: output, STRATAAI_E2E_RELEASE_HEADERS: '1', STRATAAI_E2E_RATE_PACING: '1' } });
      // Keep raw discovery diagnostics and display names private.
      writeFileSync(join(folder, `collection-${index}.log`), (result.stdout ?? '') + (result.stderr ?? ''));
      assert.equal(result.status, 0, 'Actual Playwright collection failed');
      const report = JSON.parse(readFileSync(output, 'utf8'));
      assert.deepEqual(report.config.shard, index ? { current: index, total: shardCount } : null);
      return cases(report);
    };
    return verifyPartition(collect(0), Array.from({ length: shardCount }, (_, index) => collect(index + 1)));
  } finally {
    const target = realpathSync(folder);
    assert.equal(dirname(target), parent);
    assert.ok(basename(target).startsWith('strataai-browser-shards-'));
    rmSync(target, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const result = collectAndVerify();
    console.log(`Browser shard coverage: ${result.cases} cases in ${result.files} intact files; ${result.counts.join('/')} cases across all four shards.`);
  } catch {
    console.error('Required browser shard coverage verification failed.');
    process.exitCode = 1;
  }
}
