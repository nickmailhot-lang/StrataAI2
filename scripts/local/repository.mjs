import { tmpdir } from 'node:os';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync, rmSync, renameSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { selectImpact, policy } from '../ci/select-feature-tests.mjs';
import { readdirSync } from 'node:fs';
export const git = (args, options = {}) => execFileSync('git', args, { encoding: 'utf8', ...options }).trim();
export const hash = value => createHash('sha256').update(value).digest('hex');
export function context() {
  const root = git(['rev-parse', '--show-toplevel']);
  const common = resolve(root, git(['rev-parse', '--git-common-dir']));
  const state = join(common, 'strataai-local');
  mkdirSync(state, { recursive: true, mode: 0o700 });
  return { root, common, state, worktree: hash(root).slice(0, 16) };
}
export function snapshot(root) {
  const { state } = context();
  const index = join(state, `index-${randomUUID()}`);
  const env = { ...process.env, GIT_INDEX_FILE: index };
  try {
    git(['read-tree', 'HEAD'], { cwd: root, env });
    git(['add', '--all', '--', '.'], { cwd: root, env });
    return git(['write-tree'], { cwd: root, env });
  } finally { rmSync(index, { force: true }); rmSync(`${index}.lock`, { force: true }); }
}
const paths = (args, root) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
export function localPlan(base = 'origin/main') {
  const { root } = context();
  const baseSha = git(['rev-parse', '--verify', `${base}^{commit}`], { cwd: root });
  const mergeBase = git(['merge-base', baseSha, 'HEAD'], { cwd: root });
  const untracked = paths(['ls-files', '--others', '--exclude-standard', '-z'], root);
  const changedPaths = [...new Set([
    ...paths(['diff', '--no-renames', '--name-only', '-z', mergeBase, 'HEAD'], root),
    ...paths(['diff', '--no-renames', '--name-only', '-z', 'HEAD'], root),
    ...untracked,
  ])].sort();
  const inventory = readdirSync(join(root, 'tests/browser')).filter(p => p.endsWith('.spec.ts')).map(p => `tests/browser/${p}`);
  for (const p of policy.baselineBrowser) if (!inventory.includes(p)) throw new Error(`Missing baseline browser file: ${p}`);
  const selected = selectImpact(changedPaths, inventory);
  if (untracked.some(p => !/^(docs\/|README\.md$|AGENTS\.md$)/.test(p))) {
    Object.assign(selected, { full: true, web: true, runtime: false, omitted: [] });
    selected.reasons.push('Untracked source requires complete local coverage before commit');
  }
  return { ...selected, baseSha, mergeBase, inventory,
    head: git(['rev-parse', 'HEAD']), tree: snapshot(root),
    // Full CI remains selected for shared changes. This is a local feature gate,
    // never a claim that source checks replace hosted full qualification.
    localRuntime: changedPaths.some(p => !/^(docs\/|README\.md$|AGENTS\.md$)/.test(p)),
  };
}
export function atomicJson(path, value) {
  const temporary = `${path}.${randomUUID()}.tmp`;
  writeFileSync(temporary, `${JSON.stringify(value, null, 2)}\n`, { mode: 0o600 });
  renameSync(temporary, path);
}
// Keep environment-derived paths as argv data, never part of a bash -c string.
// Each retained script belongs to one command in a unique verification run.
export function writeShellStep(directory, name, body) {
  if (!/^[a-z0-9-]+$/.test(name) || typeof body !== 'string') throw new Error('Invalid shell step');
  const path = join(directory, `${name}.sh`);
  writeFileSync(path, body, { mode: 0o600, flag: 'wx' });
  return path;
}
export function withLock(path, action) {
  mkdirSync(path); // EEXIST refuses concurrent ownership; never steal stale locks.
  try {
    writeFileSync(join(path, 'owner.json'), JSON.stringify({ pid: process.pid, cwd: process.cwd(), at: new Date().toISOString() }), { mode: 0o600 });
    return action();
  } finally { rmSync(path, { recursive: true }); }
}
export function readJson(path) { return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : null; }

// Materialize the exact candidate outside macOS protected Documents folders.
// checkout-index avoids archive export-ignore rules silently dropping tests.
export function materialize(tree) {
  const { root, state } = context();
  if (!/^[a-f0-9]{40}$/.test(tree)) throw new Error('Exact tree required');
  const directory = mkdtempSync(join(tmpdir(), 'strataai-source-'));
  const index = join(state, `checkout-${randomUUID()}`);
  const env = { ...process.env, GIT_INDEX_FILE: index };
  try {
    git(['read-tree', tree], { cwd: root, env });
    git(['checkout-index', '--all', `--prefix=${directory}/`], { cwd: root, env });
    return directory;
  } catch (error) { rmSync(directory, { recursive: true, force: true }); throw error; }
  finally { rmSync(index, { force: true }); }
}

export function evidenceInventory(directory) {
  const files = [];
  function visit(folder) {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (path === join(directory, 'result.json') || path === join(directory, 'runtime.env')) continue;
      if (entry.isSymbolicLink()) throw new Error('Evidence must not contain symbolic links');
      if (entry.isDirectory()) visit(path);
      else if (entry.isFile()) files.push({ path, sha256: hash(readFileSync(path)) });
    }
  }
  visit(directory); return files;
}

// Fixture authority/provider settings must come from the canonical CI job, never
// an agent's ambient production shell. Keep tool authentication/PATH available.
export function localRuntimeEnvironment(environment) {
  return Object.fromEntries(Object.entries(environment).filter(([key]) => !key.startsWith('STRATAAI_') && !key.startsWith('COMPOSE_')));
}
