// Execute the canonical generated feature-runtime shell bodies on the local
// machine. GHA artifact transport is replaced by locally built current-tree images.
import { readFileSync, writeFileSync, mkdirSync, openSync, closeSync, symlinkSync, rmSync, cpSync, existsSync } from 'node:fs';
import { spawn, spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { parse } from 'yaml';
import { context, withLock, snapshot, materialize, localRuntimeEnvironment, writeShellStep, executeShellStep } from './repository.mjs';
const { root, state } = context();
const tree = process.env.STRATAAI_LOCAL_TREE;
if (!/^[a-f0-9]{40}$/.test(tree ?? '') || snapshot(root) !== tree) throw new Error('Runtime requires an exact current-tree identity.');
const run = process.env.STRATAAI_LOCAL_RUN ?? randomUUID();
const directory = process.env.STRATAAI_LOCAL_EVIDENCE ?? join(state, 'runtime', run);
mkdirSync(directory, { recursive: true, mode: 0o700 });
const workflow = parse(readFileSync('.github/workflows/feature-runtime.yml', 'utf8'));
const env = { ...localRuntimeEnvironment(process.env), CI: 'true', DOCKER_DEFAULT_PLATFORM: 'linux/amd64',
  STRATAAI_LOCAL_NODE: process.execPath, COMPOSE_PROJECT_NAME: `strataai-local-${run}`.toLowerCase(),
  GITHUB_SHA: tree, GITHUB_ENV: join(directory, 'runtime.env'), RUNNER_TEMP: directory,
  STRATAAI_BUILD_REVISION: tree, STRATAAI_BUILD_VERSION: `0.1.0-local.t${tree}`,
  STRATAAI_BUILD_SOURCE: 'https://github.com/nickmailhot-lang/StrataAI2', STRATAAI_BUILD_CREATED: new Date().toISOString(),
  STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE: run, STRATAAI_IMAGE_TAG: tree,
};
for (const [key, value] of Object.entries(workflow.jobs.runtime.env)) {
  if (!value.includes('${{')) env[key] = value;
}
for (const service of ['WEB', 'API', 'WORKER']) env[`STRATAAI_${service}_IMAGE`] = `strataai-${service.toLowerCase()}:local-${tree}-${run}`;
writeFileSync(env.GITHUB_ENV, '', { mode: 0o600 });
let provider; let passed = false; let stepIndex = 0; let workspace;
const processOptions = (fd, extra = {}) => ({ cwd: workspace, shell: false, env: { ...env, ...extra }, stdio: ['ignore', fd, fd], timeout: 30 * 60 * 1000 });
function executeCommand(name, launch) {
  console.log(`Local runtime: ${name}`);
  const fd = openSync(join(directory, `runtime-${stepIndex++}.log`), 'w', 0o600);
  try {
    const result = launch(fd);
    if (result.status !== 0 || result.error) throw new Error(`Runtime stage failed: ${name}; inspect ${directory}/runtime-${stepIndex - 1}.log`);
  } finally { closeSync(fd); }
  for (const line of readFileSync(env.GITHUB_ENV, 'utf8').split('\n').filter(Boolean)) {
    const equal = line.indexOf('='); const key = line.slice(0, equal);
    if (equal < 1 || !/^[A-Z][A-Z0-9_]*$/.test(key)) throw new Error('Unsupported canonical runtime environment output');
    env[key] = line.slice(equal + 1);
  }
}
function execute(name, executable, argv, extra) {
  executeCommand(name, fd => spawnSync(executable, argv, processOptions(fd, extra)));
}
withLock(join(state, 'canonical-runtime.lock'), () => {
  // Canonical fixtures use fixed ports. Serialize their runtime across worktrees;
  // retain unique projects/images/volumes and refuse occupied external ports.
  const preflight = spawnSync(process.execPath, ['--input-type=module', '-e', `import net from 'node:net'; for (const port of [8080,8081,8088,9464,19090,18082,18083]) await new Promise((resolve,reject)=>{ const s=net.createServer();s.once('error',reject);s.listen(port,'0.0.0.0',()=>s.close(resolve)); });`], { encoding: 'utf8' });
  if (preflight.status !== 0) throw new Error('Canonical fixture ports are occupied; stop your own conflicting runtime before verifying.');
  try {
    workspace = materialize(tree);
    env.PATH = `${join(workspace, 'scripts/local/bin')}:${process.env.PATH}`;
    symlinkSync(join(root, 'node_modules'), join(workspace, 'node_modules'), 'dir');
    for (const service of ['web','api','worker']) execute(`build-${service}`, 'docker', ['build', '--platform', 'linux/amd64', '--file', `Dockerfile.${service}`,
      ...['REVISION','VERSION','SOURCE','CREATED'].flatMap(key => ['--build-arg', `STRATAAI_BUILD_${key}=${env[`STRATAAI_BUILD_${key}`]}`]),
      '--tag', env[`STRATAAI_${service.toUpperCase()}_IMAGE`], '.']);
    execute('pull-exact-runtime-platform-postgres', 'docker', ['pull', '--platform', 'linux/amd64', 'pgvector/pgvector:pg17']);
    for (const step of workflow.jobs.runtime.steps) {
      if (step.uses) {
        if (!/^(actions\/(checkout|download-artifact|upload-artifact|setup-node))@/.test(step.uses)) throw new Error(`Unmapped runtime action ${step.uses}`);
        continue;
      }
      if (['Verify image archive checksums', 'Load exact built images', 'Install browser test dependencies'].includes(step.name)) continue;
      if (['Prepare Retain browser failure evidence', 'Prepare Retain scoped browser performance measurements', 'Capture container state and logs', 'Prepare Upload integration diagnostics', 'Stop release topology'].includes(step.name)) continue; // local diagnostics/cleanup below
      if (step.if) throw new Error(`Unsupported runtime condition ${step.if}`);
      let body = step.run;
      if (step.name === 'Prepare isolated identity mail transport and ephemeral signing keys') {
        const launch = 'python3 scripts/ci/fake-identity-provider.py > /tmp/identity-provider.log 2>&1 &';
        if (!body.includes(launch)) throw new Error('Identity provider launch drift; review local ownership adapter.');
        const fd = openSync(join(directory, 'identity-provider.log'), 'w', 0o600);
        provider = spawn('python3', ['scripts/ci/fake-identity-provider.py'], { cwd: workspace, env, stdio: ['ignore', fd, fd] }); closeSync(fd);
        body = body.replace(launch, '');
      }
      const stepEnv = {};
      for (const [key, value] of Object.entries(step.env ?? {})) {
        stepEnv[key] = value === '${{ inputs.browser }}' ? process.env.STRATAAI_SELECTED_BROWSER : value;
        if (typeof stepEnv[key] !== 'string' || stepEnv[key].includes('${{')) throw new Error(`Unresolved runtime environment: ${key}`);
      }
      if (body.includes('${{')) throw new Error('Unresolved canonical runtime expression');
      const script = writeShellStep(directory, `runtime-${stepIndex}`, body);
      executeCommand(step.name, fd => executeShellStep(script, {
        ...processOptions(fd, stepEnv), stdio: ['ignore', fd, fd],
      }));
    }
    passed = true;
  } finally {
    if (provider) provider.kill();
    // Commands remain restricted to this run's unique Compose project.
    try { execute('runtime-diagnostics', 'docker', ['compose', '-f', 'compose.release.yml', 'logs', '--no-color']); } catch { /* original failure retained */ }
    let cleanupError;
    try { if (workspace) execute('stop-owned-runtime', 'docker', ['compose', '-f', 'compose.release.yml', '-f', 'compose.metrics.yml', 'down', ...(passed ? ['-v'] : []), '--remove-orphans']); }
    catch (error) { cleanupError = error; console.error(`Owned runtime cleanup requires inspection: ${env.COMPOSE_PROJECT_NAME}`); }
    if (workspace) {
      for (const path of ['test-results', 'playwright-report', 'artifacts/browser-performance']) {
        const source = join(workspace, path);
        if (existsSync(source)) cpSync(source, join(directory, 'browser-evidence', path), { recursive: true });
      }
      rmSync(workspace, { recursive: true, force: true });
    }
    if (cleanupError && passed) throw cleanupError;
    if (!passed) console.error(`Failed runtime volumes retained under ${env.COMPOSE_PROJECT_NAME}; evidence ${directory}`);
  }
});
