import { join, resolve, relative, isAbsolute } from 'node:path';
import { context, git, readJson, atomicJson, withLock } from './repository.mjs';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const shared = ['AGENTS.md', 'package.json', 'package-lock.json', '.github', '.githooks', 'scripts', 'db/migrations', 'docs/delivery', 'docs/ux/delivery-report.md', 'apps/web/src/app', 'apps/web/src/components', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'];
const canonical = path => path.normalize('NFC').toLowerCase();
const contains = (owner, path) => canonical(path) === canonical(owner) || canonical(path).startsWith(`${canonical(owner)}/`);
const overlaps = (a, b) => contains(a, b) || contains(b, a);
function safePath(value) {
  if (!value || isAbsolute(value) || value.includes('\\') || value.split('/').some(p => ['..', '.', ''].includes(p)) || (canonical(value) === '.git' || canonical(value).startsWith('.git/'))) throw new Error('Use explicit repository-relative owned paths without traversal.');
  return value;
}
export function coordinate(action, input) {
  const { state, root } = context();
  const path = join(state, 'agents.json');
  return withLock(join(state, 'agents.lock'), () => {
    const registry = readJson(path) ?? { schemaVersion: 1, integrationOwner: null, tasks: [], history: [] };
    const owner = input.owner;
    if (action !== 'status' && !/^[A-Za-z0-9_-]{1,80}$/.test(owner ?? '')) throw new Error('A stable --owner is required.');
    if (action === 'init') {
      if (registry.integrationOwner && registry.integrationOwner !== owner) throw new Error('Integration owner already assigned; explicit handoff required.');
      registry.integrationOwner = owner;
    } else if (action === 'claim') {
      if (!registry.integrationOwner) throw new Error('Initialize an integration owner before claims.');
      if (!/^[A-Za-z0-9_-]{1,100}$/.test(input.id ?? '') || registry.tasks.some(t => t.id === input.id)) throw new Error('Unique task ID required.');
      const ownedPaths = input.paths?.map(safePath);
      if (!ownedPaths?.length) throw new Error('At least one --paths entry is required.');
      if (owner !== registry.integrationOwner && ownedPaths.some(p => shared.some(s => overlaps(p, s)))) throw new Error('Shared files require the integration owner.');
      const branch = git(['symbolic-ref', '--short', 'HEAD']);
      if (branch === 'main') throw new Error('Claim from an isolated feature branch/worktree.');
      const active = registry.tasks.filter(t => t.status !== 'released');
      if (active.some(t => t.worktree === root || t.branch === branch || t.paths.some(a => ownedPaths.some(b => overlaps(a, b))))) throw new Error('Worktree, branch or owned paths overlap an active task; arrange explicit handoff.');
      registry.tasks.push({ id: input.id, owner, branch, worktree: root, baseSha: git(['rev-parse', 'HEAD']), paths: ownedPaths,
        dependencies: input.dependencies ?? [], status: 'claimed', runtimeProject: `strataai-${randomUUID().replaceAll('-', '').slice(0, 16)}` });
    } else if (action === 'handoff') {
      const task = registry.tasks.find(t => t.id === input.id);
      if (!task || task.owner !== owner || task.status !== 'claimed') throw new Error('Only the current claimant can hand off a claimed task.');
      const handoff = input.handoff;
      for (const key of ['acceptance', 'evidence', 'limitations', 'dependencies']) if (!Array.isArray(handoff?.[key])) throw new Error(`Handoff needs ${key} array.`);
      if (!handoff.acceptance.length || !handoff.evidence.length) throw new Error('Acceptance and evidence cannot be empty.');
      task.handoff = handoff; task.status = 'review';
    } else if (action === 'release') {
      if (owner !== registry.integrationOwner) throw new Error('Only the integration owner releases claims after integration or explicit cancellation.');
      const task = registry.tasks.find(t => t.id === input.id);
      if (!task || task.status === 'released' || !input.reason) throw new Error('Active task and release reason required.');
      task.status = 'released'; task.releaseReason = input.reason;
    } else if (action !== 'status') throw new Error('Use init, claim, handoff, release or status.');
    if (action !== 'status') {
      registry.history.push({ at: new Date().toISOString(), action, owner, id: input.id ?? null, reason: input.reason ?? null });
      atomicJson(path, registry);
    }
    return registry;
  });
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [action, ...args] = process.argv.slice(2); const input = {};
  for (let i = 0; i < args.length; i += 2) {
    if (!args[i].startsWith('--') || !args[i + 1]) throw new Error('Expected --key value');
    const key = args[i].slice(2); input[key] = ['paths', 'dependencies'].includes(key) ? args[i + 1].split(',') : key === 'handoff' ? readJson(resolve(args[i + 1])) : args[i + 1];
  }
  console.log(JSON.stringify(coordinate(action, input), null, 2));
}

export function assertOwnership(changedPaths) {
  const registry = coordinate('status', {});
  const { root } = context(); const branch = git(['symbolic-ref', '--short', 'HEAD']);
  const task = registry.tasks.find(t => t.worktree === root && t.branch === branch && t.status !== 'released');
  if (!task) throw new Error('Claim this branch/worktree before verifying or committing changes.');
  const outside = changedPaths.filter(p => !task.paths.some(owned => contains(owned, p)));
  if (outside.length) throw new Error(`Changes outside claimed paths: ${outside.join(', ')}. Arrange an explicit ownership handoff.`);
  return { id: task.id, owner: task.owner, branch, paths: task.paths };
}
