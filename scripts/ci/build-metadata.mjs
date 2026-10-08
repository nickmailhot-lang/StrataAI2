import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

function requireValue(value, pattern, message) {
  if (typeof value !== 'string' || /[\r\n]/.test(value) || !pattern.test(value)) throw new Error(message);
  return value;
}

// SemVer 2.0: numeric core/pre-release identifiers cannot have leading zeros.
function semanticVersion(value) {
  requireValue(value, /^[0-9A-Za-z.+-]{1,80}$/, 'Invalid semantic build version.');
  const match = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/.exec(value);
  if (!match || match[4]?.split('.').some(part => /^[0-9]+$/.test(part) && part.length > 1 && part[0] === '0')) {
    throw new Error('Invalid semantic build version.');
  }
  return value;
}

export function createBuildMetadata(env, checkedOutSha, packageVersion, createdAt) {
  const commitSha = requireValue(env.GITHUB_SHA, /^(?:[a-f0-9]{40}|[a-f0-9]{64})$/, 'Invalid triggering commit.');
  if (checkedOutSha !== commitSha) throw new Error('Checkout does not match the triggering commit.');
  const repository = requireValue(env.GITHUB_REPOSITORY, /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/, 'Invalid source repository.');
  const workflowRunId = requireValue(env.GITHUB_RUN_ID, /^[1-9][0-9]*$/, 'Invalid workflow run identity.');
  const workflowRunNumber = requireValue(env.GITHUB_RUN_NUMBER, /^[1-9][0-9]*$/, 'Invalid workflow run number.');
  const workflowRunAttempt = requireValue(env.GITHUB_RUN_ATTEMPT, /^[1-9][0-9]*$/, 'Invalid workflow run attempt.');
  requireValue(env.GITHUB_REF, /^refs\/(?:heads|pull|tags)\/[^\r\n]+$/, 'Invalid workflow ref.');
  if (typeof createdAt !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(createdAt)
    || !Number.isFinite(Date.parse(createdAt)) || new Date(createdAt).toISOString() !== createdAt) {
    throw new Error('Invalid metadata creation clock.');
  }
  let version;
  let releaseVersion = null;
  if (env.GITHUB_REF.startsWith('refs/tags/')) {
    if (!env.GITHUB_REF.startsWith('refs/tags/v')) throw new Error('Release tags require the v prefix.');
    releaseVersion = semanticVersion(env.GITHUB_REF.slice('refs/tags/v'.length));
    version = releaseVersion;
  } else {
    const base = semanticVersion(packageVersion);
    const [core, build] = base.split('+');
    version = semanticVersion(`${core}${core.includes('-') ? '.' : '-'}${workflowRunNumber}${build ? `+${build}` : ''}`);
  }
  return {
    repository, commitSha, shortSha: commitSha.slice(0, 12), workflowRunId, workflowRunNumber,
    workflowRunAttempt, version, releaseVersion, imageTag: commitSha, createdAt,
    images: { web: `strataai-web:${commitSha}`, api: `strataai-api:${commitSha}`, worker: `strataai-worker:${commitSha}` },
  };
}

export function metadataOutputs(metadata) {
  return Object.entries({ revision: metadata.commitSha, short_sha: metadata.shortSha,
    version: metadata.version, image_tag: metadata.imageTag, workflow_run_id: metadata.workflowRunId,
    repository: metadata.repository, created_at: metadata.createdAt })
    .map(([name, value]) => `${name}=${value}\n`).join('');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const checkedOutSha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    const metadata = createBuildMetadata(process.env, checkedOutSha,
      JSON.parse(readFileSync('package.json', 'utf8')).version, new Date().toISOString());
    if (!process.env.GITHUB_OUTPUT) throw new Error('Missing metadata output destination.');
    writeFileSync('build-metadata.json', `${JSON.stringify(metadata, null, 2)}\n`);
    appendFileSync(process.env.GITHUB_OUTPUT, metadataOutputs(metadata));
    console.log('Verified exact-commit build metadata emitted.');
  } catch {
    // Workflow context and host diagnostics must never be echoed on refusal.
    console.error('Build metadata validation or publication failed.');
    process.exitCode = 1;
  }
}
