import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, appendFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireFeatureChecks } from './require-feature-checks.mjs';

const digest = value => createHash('sha256').update(value).digest('hex');
const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
const workflowPath = '.github/workflows/feature-ci.yml';
export function automaticRequest(env, event = {}) {
  return env.GITHUB_EVENT_NAME === 'pull_request' && env.GITHUB_RUN_ATTEMPT === '1'
    && !(event.action === 'labeled' && event.label?.name === 'full-ci');
}
export function identity(plan, env, event) {
  if (!['docs', 'feature', 'full'].includes(plan.tier) || ['full', 'web', 'runtime'].some(k => typeof plan[k] !== 'boolean')) throw new Error('Invalid coverage policy');
  const commit = git('rev-parse', 'HEAD');
  if (commit !== env.GITHUB_SHA || plan.revision !== commit) throw new Error('Tested integration revision mismatch');
  const base = event.pull_request?.base.sha ?? event.merge_group?.base_sha ?? git('rev-parse', 'HEAD^');
  if (!/^[a-f0-9]{40}$/.test(base)) throw new Error('Missing integration base');
  return {
    repository: env.GITHUB_REPOSITORY, commit, tree: git('rev-parse', 'HEAD^{tree}'), base,
    source: event.pull_request?.head.sha ?? commit,
    // Hash every checked-in workflow and selector/runtime script, not a mutable version label.
    policy: digest(git('ls-tree', '-r', 'HEAD', '.github', 'scripts', 'package.json', 'package-lock.json')),
    scope: { tier: plan.tier, full: plan.full, web: plan.web, runtime: plan.runtime, browser: plan.browser, commands: plan.commands },
  };
}
export const qualificationKey = request => digest(JSON.stringify(request));
export function reusable(request, proof, run, current) {
  return run.id !== Number(current.runId) && run.status === 'completed' && run.conclusion === 'success'
    && run.event === 'pull_request' && run.workflow_id === current.workflowId && run.path === workflowPath
    && run.head_sha === request.source && proof.schemaVersion === 1 && proof.result === 'success'
    && proof.runId === run.id && proof.runAttempt === run.run_attempt
    && proof.reused === false && qualificationKey(proof.identity) === qualificationKey(request);
}
export async function findReusable(request, current, api, readProof) {
  const name = `ci-qualification-${qualificationKey(request)}`;
  for (let page = 1; ; page++) {
    const listing = await api(`/actions/artifacts?name=${name}&per_page=100&page=${page}`);
    if (!Array.isArray(listing.artifacts)) throw new Error('Invalid artifact listing');
    for (const artifact of listing.artifacts) {
      if (artifact.expired || artifact.name !== name || !artifact.workflow_run?.id) continue;
      const run = await api(`/actions/runs/${artifact.workflow_run.id}`);
      if (run.status !== 'completed' || run.conclusion !== 'success' || run.id === Number(current.runId)) continue;
      const proof = await readProof(artifact);
      if (reusable(request, proof, run, current)) return { artifactId: artifact.id, runId: run.id };
    }
    if (listing.artifacts.length < 100) return null;
  }
}
export function completeChecks(env, request, proof, run, current) {
  const scope = request.scope;
  for (const [key, field] of [['web','WEB_SELECTED'], ['runtime','RUNTIME_SELECTED'], ['full','FULL_SELECTED']])
    if (String(scope[key]) !== env[field]) throw new Error('Coverage output mismatch');
  if (env.REUSED === 'false') { requireFeatureChecks(env); return; }
  if (env.REUSED !== 'true' || env.SELECTION_RESULT !== 'success'
    || ['WEB_RESULT','RUNTIME_RESULT','FULL_RESULT'].some(k => env[k] !== 'skipped')
    || !reusable(request, proof, run, current)) throw new Error('Missing or stale successful qualification');
}

async function main() {
  const env = process.env;
  if (!/^[\w.-]+\/[\w.-]+$/.test(env.GITHUB_REPOSITORY ?? '') || !/^\d+$/.test(env.GITHUB_RUN_ID ?? '')) throw new Error('Invalid repository/run');
  const root = `https://api.github.com/repos/${env.GITHUB_REPOSITORY}`;
  async function response(path) {
    const r = await fetch(root + path, { headers: { Authorization: `Bearer ${env.GITHUB_TOKEN}`, Accept: 'application/vnd.github+json' }, signal: AbortSignal.timeout(30_000) });
    if (!r.ok) throw new Error(`Qualification API failed: ${r.status}`);
    return r;
  }
  const api = async path => (await response(path)).json();
  async function readProof(artifact) {
    if (artifact.expired || artifact.size_in_bytes > 1024 * 1024 || !/^sha256:[a-f0-9]{64}$/.test(artifact.digest ?? '')) throw new Error('Invalid qualification artifact');
    const bytes = Buffer.from(await (await response(`/actions/artifacts/${artifact.id}/zip`)).arrayBuffer());
    if (bytes.length > 1024 * 1024 || `sha256:${digest(bytes)}` !== artifact.digest) throw new Error('Qualification artifact digest mismatch');
    const directory = mkdtempSync(join(tmpdir(), 'strataai-ci-proof-'));
    try {
      const path = join(directory, 'proof.zip'); writeFileSync(path, bytes, { mode: 0o600 });
      return JSON.parse(execFileSync('unzip', ['-p', path, 'ci-qualification.json'], { encoding: 'utf8', maxBuffer: 1024 * 1024, timeout: 10_000 }));
    } finally { rmSync(directory, { recursive: true, force: true }); }
  }
  const event = JSON.parse(readFileSync(env.GITHUB_EVENT_PATH, 'utf8'));
  const plan = JSON.parse(readFileSync('feature-plan.json', 'utf8'));
  const request = identity(plan, env, event);
  const currentRun = await api(`/actions/runs/${env.GITHUB_RUN_ID}`);
  const current = { runId: env.GITHUB_RUN_ID, workflowId: currentRun.workflow_id };
  if (process.argv[2] === 'lookup') {
    // Network or unavailable evidence means fresh work, never manufactured success.
    let prior = null;
    if (automaticRequest(env, event)) {
      try { prior = await findReusable(request, current, api, readProof); }
      catch (error) { console.error(`Fresh checks required: ${error.message}`); }
    }
    writeFileSync('ci-request.json', JSON.stringify({ identity: request, prior }, null, 2) + '\n');
    appendFileSync(env.GITHUB_OUTPUT, `reused=${!!prior}\n`);
    return;
  }
  if (process.argv[2] !== 'complete') throw new Error('Use lookup or complete');
  const saved = JSON.parse(readFileSync('ci-request.json', 'utf8'));
  if (qualificationKey(saved.identity) !== qualificationKey(request)) throw new Error('Changed integration identity');
  let proof, run;
  if (env.REUSED === 'true') {
    if (!automaticRequest(env, event) || !saved.prior) throw new Error('Manual requests require fresh checks');
    const artifact = await api(`/actions/artifacts/${saved.prior.artifactId}`);
    run = await api(`/actions/runs/${saved.prior.runId}`); proof = await readProof(artifact);
  }
  completeChecks(env, request, proof, run, current);
  const result = { schemaVersion: 1, identity: request, result: 'success', reused: env.REUSED === 'true', runId: Number(env.GITHUB_RUN_ID), runAttempt: Number(env.GITHUB_RUN_ATTEMPT), prior: saved.prior };
  writeFileSync('ci-qualification.json', JSON.stringify(result, null, 2) + '\n');
  appendFileSync(env.GITHUB_OUTPUT, `key=${qualificationKey(request)}\n`);
  appendFileSync(env.GITHUB_STEP_SUMMARY, `${request.scope.tier} qualification: ${result.reused ? `reused completed successful run ${saved.prior.runId}` : 'fresh selected checks passed'}. Integration commit ${request.commit}, tree ${request.tree}, base ${request.base}.\n`);
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
