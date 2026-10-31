import { tmpdir } from 'node:os';
import { parse } from 'yaml';
import { assertOwnership } from './agents.mjs';
import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync, openSync, closeSync, appendFileSync, realpathSync } from 'node:fs';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { context, localPlan, snapshot, git, hash, atomicJson, withLock, evidenceInventory, writeShellStep, executeShellStep } from './repository.mjs';
import { doctor } from './doctor.mjs';
const args = process.argv.slice(2);
const allowed = new Set(['--base', '--mode', '--plan', '--all-browser']);
for (let i = 0; i < args.length; i++) { if (!allowed.has(args[i])) throw new Error(`Unknown option ${args[i]}`); if (['--base','--mode'].includes(args[i])) { if (!args[++i] || args[i].startsWith('--')) throw new Error('Missing option value'); } }
const baseRef = args.includes('--base') ? args[args.indexOf('--base') + 1] : 'origin/main';
const mode = args.includes('--mode') ? args[args.indexOf('--mode') + 1] : 'final';
if (!['fast', 'final'].includes(mode)) throw new Error('Mode must be fast or final');
const { root, state, worktree } = context();
const plan = localPlan(baseRef);
if (args.includes('--plan')) { console.log(JSON.stringify(plan, null, 2)); process.exit(0); }
const lock = join(state, `verify-${worktree}.lock`);
withLock(lock, () => {
  const destination = join(state, `verified-${worktree}.json`);
  rmSync(destination, { force: true });
  const runId = `${Date.now()}-${randomUUID().slice(0, 8)}`;
  const directory = join(state, 'runs', runId); mkdirSync(directory, { recursive: true, mode: 0o700 });
  const startedAt = new Date().toISOString(); const started = Date.now(); const results = [];
  const receipt = { schemaVersion: 1, mode, runId, baseRef, baseSha: plan.baseSha, head: plan.head, tree: plan.tree, plan,
    startedAt, platform: process.platform, architecture: process.arch, runtimePlatform: 'linux/amd64', results, passed: false,
    qualification: 'Local feature evidence only; required-ci and exact-revision full release qualification remain required.' };
  const persist = () => atomicJson(join(directory, 'result.json'), receipt);
  const processOptions = (fd, options = {}) => ({ cwd: root, shell: false, stdio: ['ignore', fd, fd],
    timeout: 60 * 60 * 1000, env: { ...process.env, TMPDIR: realpathSync(tmpdir()), DOTNET_CLI_TELEMETRY_OPTOUT: '1', STRATAAI_LOCAL_RUN: runId, ...options.env } });
  function record(name, command, launch, inputFile) {
    const at = Date.now(); const log = join(directory, `${results.length}-${name}.log`);
    console.log(`Running ${name} ...`);
    const fd = openSync(log, 'w', 0o600);
    let result;
    try { result = launch(fd); } finally { closeSync(fd); }
    if (result.error) appendFileSync(log, `\n${result.error.message}`);
    const record = { name, command, ...(inputFile ? { inputFile, inputSha256: hash(readFileSync(inputFile)) } : {}), passed: result.status === 0 && !result.error, exitCode: result.status,
      durationMs: Date.now() - at, log, sha256: hash(readFileSync(log)) };
    results.push(record); persist(); console.log(`${record.passed ? 'PASS' : 'FAIL'} ${name} (${(record.durationMs / 1000).toFixed(1)}s): ${log}`);
    if (!record.passed) throw new Error(`${name} failed; retained original evidence at ${log}`);
  }
  function run(name, executable, argv, options) {
    record(name, [executable, ...argv], fd => spawnSync(executable, argv, processOptions(fd, options)));
  }
  function runShellStep(name, body, options) {
    const script = writeShellStep(directory, `${results.length}-${name}`, body);
    record(name, ['bash', '--noprofile', '--norc', '-euo', 'pipefail', '/dev/fd/3'], fd => executeShellStep(script, {
      ...processOptions(fd, options), stdio: ['ignore', fd, fd],
    }), script);
  }
  try {
    receipt.task = assertOwnership(plan.changedPaths);
    const tools = doctor({ tier: plan.tier }); receipt.tools = tools; persist();
    if (!tools.passed) throw new Error('Toolchain unavailable or mismatched. Run npm run dev:doctor.');
    const feature = parse(readFileSync(join(root, '.github/workflows/feature-ci.yml'), 'utf8'));
    const contractStep = feature.jobs.select.steps.find(s => s.name === 'Validate CI contracts and immutable full-suite coverage');
    if (!contractStep?.run || contractStep.run.includes('${{')) throw new Error('Review changed feature contract command before local execution.');
    if (mode === 'final' && plan.full) {
      const contracts = readdirSync(join(root, 'tests')).filter(p => p.endsWith('.test.mjs')).sort().map(p => `tests/${p}`);
      run('repository-contracts', process.execPath, ['--test', '--test-concurrency=1', ...contracts]);
    } else runShellStep('repository-contracts', contractStep.run);
    run('ledger', process.execPath, ['scripts/delivery-progress.mjs', 'validate', '--base-ref', plan.baseSha]);
    run('integration-inventory', process.execPath, ['scripts/ci/verify-integration-suites.mjs']);
    run('runtime-drift', process.execPath, ['scripts/ci/generate-feature-runtime.mjs', '--check']);
    if (plan.tier !== 'docs') {
    run('web-types', 'npm', ['run', 'typecheck']);
    run('browser-types', 'npm', ['run', 'typecheck:browser']);
    run('lint', 'npm', ['run', 'lint']);
    if (mode === 'final' && plan.full) run('web-unit', 'npm', ['test', '--', '--maxWorkers=1']);
    else for (const name of ['Critical unit baseline', 'Affected unit and component tests']) {
      const step = feature.jobs.web.steps.find(s => s.name === name);
      if (!step?.run || step.run.includes('${{')) throw new Error('Review changed feature unit command before local execution.');
      runShellStep(name === 'Critical unit baseline' ? 'web-unit-baseline' : 'web-unit-affected', step.run, { env: { PR_BASE: plan.mergeBase } });
    }
    run('web-build', 'npm', ['run', 'build']);
    }
    if (mode === 'final') {
      if (plan.full) {
      run('dotnet-restore', 'dotnet', ['restore', 'StrataAI2.slnx', '--locked-mode']);
      run('dotnet-build', 'dotnet', ['build', 'StrataAI2.slnx', '-c', 'Release', '--no-restore', '-warnaserror']);
      for (const project of ['Domain']) run(`dotnet-${project}`, 'dotnet', ['test', '--project', `tests/StrataAI.${project}.Tests/StrataAI.${project}.Tests.csproj`, '-c', 'Release', '--no-build']);
      run('dotnet-Api-linux', process.execPath, ['scripts/local/source-tests.mjs']);
      run('python-contracts', 'python3', ['-m', 'unittest', 'discover', '-s', 'tests', '-p', '*_test.py']);
      } else run('python-contracts', 'python3', ['-m', 'unittest', 'discover', '-s', 'tests', '-p', 'release_artifacts_test.py']);
      run('secret-scan', process.execPath, ['scripts/local/secret-scan.mjs']);
      if (plan.localRuntime) {
        if (plan.full) run('persistence', process.execPath, ['scripts/local/persistence.mjs']);
        const browser = args.includes('--all-browser') ? plan.inventory : plan.browser;
        run('feature-runtime', process.execPath, ['scripts/local/runtime.mjs'], { env: { STRATAAI_LOCAL_TREE: plan.tree, STRATAAI_SELECTED_BROWSER: JSON.stringify(browser), STRATAAI_LOCAL_EVIDENCE: directory } });
      }
    }
    if (snapshot(root) !== plan.tree || git(['rev-parse', 'HEAD']) !== plan.head || git(['rev-parse', `${baseRef}^{commit}`]) !== plan.baseSha) throw new Error('Source, HEAD or base changed during verification. Rerun on a stable candidate.');
    receipt.artifacts = evidenceInventory(directory);
    receipt.passed = true; receipt.durationMs = Date.now() - started; receipt.completedAt = new Date().toISOString(); persist();
    if (mode === 'final') atomicJson(destination, receipt);
    console.log(`${mode} local gate passed for tree ${plan.tree}; evidence ${directory}`);
  } catch (error) { receipt.failure = error.message; receipt.durationMs = Date.now() - started; persist(); throw error; }
});
