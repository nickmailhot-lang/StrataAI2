import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync, appendFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { changedPaths } from '../../scripts/ci/select-feature-tests.mjs';
import { selectFeatureGate } from './selection.mjs';
import { selectWebTests } from './web-selection.mjs';
import { partitionWebTests } from './web-partitions.mjs';
import { selectBackendTests } from './backend-selection.mjs';

export function createPlan(paths, root, options = {}) {
  for(const key of ['measureBackend','coldCache'])if(options[key]!==undefined && typeof options[key]!=='boolean')throw new Error('Invalid manual measurement input');
  if((options.measureBackend || options.coldCache) && options.event!=='manual')throw new Error('Measurement requires explicit manual request');
  const browser = readdirSync(resolve(root, 'tests/browser')).filter(f => f.endsWith('.spec.ts')).map(f => `tests/browser/${f}`);
  const plan = selectFeatureGate(paths, browser, options);
  if(plan.full && (options.measureBackend || options.coldCache))throw new Error('Fast measurements cannot alter full qualification');
  if(options.measureBackend)Object.assign(plan,{dotnet:true,api:true,profile:'feature'});
  if(options.measureBackend || options.coldCache)plan.measurement={backend:!!options.measureBackend,coldCache:!!options.coldCache,scope:'Additive development measurement; actual changed paths and all selected obligations retained'};
  plan.tier = plan.full ? 'full' : plan.profile;
  plan.runtime = false;
  plan.browser = plan.affectedBrowser;
  plan.commands = []; // Comprehensive production commands remain in full sanity.
  plan.omitted = plan.deferred;
  plan.webTests = !plan.full && plan.web ? selectWebTests(root, plan.changedPaths).files : [];
  plan.webPartitions = plan.webTests.length ? partitionWebTests(plan.webTests) : [];
  const folder = 'tests/StrataAI.Api.Tests/';
  plan.apiSelection = !plan.full && plan.api ? selectBackendTests(plan.changedPaths,
    readdirSync(resolve(root, folder)).map(f => folder + f), file => readFileSync(resolve(root, file), 'utf8')) : null;
  plan.revision = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim();
  plan.sourceTree = execFileSync('git', ['rev-parse', 'HEAD^{tree}'], { cwd: root, encoding: 'utf8' }).trim();
  delete plan.scopeFingerprint;
  plan.scopeFingerprint = createHash('sha256').update(JSON.stringify(plan)).digest('hex');
  return plan;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const env = process.env, event = JSON.parse(readFileSync(env.GITHUB_EVENT_PATH, 'utf8'));
  const base = event.pull_request?.base.sha ?? event.merge_group?.base_sha ?? event.before
    ?? (env.GITHUB_EVENT_NAME === 'workflow_dispatch' ? execFileSync('git', ['rev-parse', 'origin/main'], { encoding: 'utf8' }).trim() : undefined);
  const paths = changedPaths(base);
  const plan = createPlan(paths, '.', {
    event: env.GITHUB_EVENT_NAME === 'merge_group' ? 'merge-queue' : env.GITHUB_EVENT_NAME === 'workflow_dispatch' ? 'manual' : 'pull-request',
    forceFull: env.FORCE_FULL === 'true' || event.pull_request?.labels?.some(l => l.name === 'full-ci'),
    measureBackend: env.MEASURE_BACKEND==='true',coldCache:env.FAST_CI_COLD_CACHE==='true',
  });
  const directory = env.FAST_CI_PLAN_DIR ?? 'artifacts/fast';
  mkdirSync(directory, {recursive:true});
  writeFileSync(resolve(directory, 'feature-plan.json'), JSON.stringify(plan, null, 2) + '\n');
  for (const key of ['full','web','dotnet','api','runtime','scopeFingerprint']) appendFileSync(env.GITHUB_OUTPUT, `${key}=${plan[key]}\n`);
  appendFileSync(env.GITHUB_STEP_SUMMARY, `${plan.profile} scope ${plan.scopeFingerprint}: web=${plan.web}, backend=${plan.dotnet}, full=${plan.full}. Deferred: ${plan.deferred.join('; ') || 'none'}. Feature evidence is not production qualification.\n`);
}
