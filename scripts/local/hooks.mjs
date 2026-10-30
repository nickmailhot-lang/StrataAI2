import { assertOwnership } from './agents.mjs';
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { context, git, snapshot, readJson, hash, localPlan } from './repository.mjs';
const { root, state, worktree } = context();
const action = process.argv[2];
if (action === 'install') {
  const current = (() => { try { return git(['config', '--get', 'core.hooksPath']); } catch { return ''; } })();
  if (current && current !== '.githooks') throw new Error(`Existing hooksPath ${current}; integrate explicitly instead of overwriting it.`);
  git(['config', 'core.hooksPath', '.githooks']);
  console.log('Installed local verification hooks for this repository and its worktrees.');
} else {
  const receipt = readJson(join(state, `verified-${worktree}.json`));
  if (!receipt?.passed || receipt.mode !== 'final' || !receipt.results?.length) throw new Error('No final local pass. Run npm run verify:local -- --base origin/main.');
  const expected = action === 'pre-push' ? git(['rev-parse', 'HEAD^{tree}']) : git(['write-tree']);
  if (expected !== receipt.tree || snapshot(root) !== receipt.tree) throw new Error('Stale verification or partially staged/untracked changes. Stage the exact candidate and rerun verification.');
  if (git(['rev-parse', `${receipt.baseRef}^{commit}`]) !== receipt.baseSha) throw new Error('Base changed; reverify the integration candidate.');
  assertOwnership(localPlan(receipt.baseRef).changedPaths);
  for (const artifact of receipt.artifacts ?? []) if (!existsSync(artifact.path) || hash(readFileSync(artifact.path)) !== artifact.sha256) throw new Error('Nested verification evidence changed.');
  for (const result of receipt.results) {
    if (!result.passed || !existsSync(result.log) || hash(readFileSync(result.log)) !== result.sha256) throw new Error('Required verification evidence missing or changed.');
  }
  if (action === 'pre-push') {
    const head = git(['rev-parse', 'HEAD']);
    for (const line of readFileSync(0, 'utf8').trim().split('\n').filter(Boolean)) {
      const [localRef, localSha, remoteRef, remoteSha] = line.split(' ');
      if (/^0+$/.test(localSha) || localRef === '(delete)') throw new Error('Branch deletion is outside the verified push workflow.');
      if (remoteRef !== `refs/heads/${git(['symbolic-ref', '--short', 'HEAD'])}` || localSha !== head) throw new Error('Only the verified HEAD branch may be pushed.');
      if (remoteRef === 'refs/heads/main') throw new Error('Push a feature branch and use reviewed integration; direct main pushes are refused.');
      if (!/^0+$/.test(remoteSha)) git(['merge-base', '--is-ancestor', remoteSha, localSha]);
    }
  } else if (action !== 'pre-commit') throw new Error('Unknown hook action');
  console.log('Exact tested tree and local evidence verified. Hosted CI/acceptance remain separate.');
}
