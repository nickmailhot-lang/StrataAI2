// Execute every discovered API method in bounded Linux processes on small Macs.
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { rmSync, mkdirSync, openSync, closeSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { context, snapshot, materialize, atomicJson } from './repository.mjs';
const c = context(), tree = snapshot(c.root), root = materialize(tree);
const label = randomUUID(), name = `strataai-source-${label}`;
const runId = process.env.STRATAAI_LOCAL_RUN;
if (runId && !/^[0-9]+-[a-f0-9]+$/.test(runId)) throw new Error('Invalid local run identity');
const evidence = runId ? join(c.state, 'runs', runId, 'api-source') : join(c.state, 'source-tests', label);
mkdirSync(evidence, { recursive: true, mode: 0o700 });
const executable = '/tmp/source/tests/StrataAI.Api.Tests/bin/Release/net10.0/StrataAI.Api.Tests';
let created = false, copied = false;
const docker = (args, timeout = 30_000) => {
  const r = spawnSync('docker', args, { encoding: 'utf8', timeout, maxBuffer: 32 * 1024 * 1024 });
  if (r.status !== 0 || r.error) throw new Error(`Docker command failed: ${args[0]} ${r.error?.message ?? r.stderr}`);
  return r.stdout.trim();
};
function run(step, args) {
  console.log(`Running ${step}; evidence ${evidence}`);
  const log = join(evidence, `${step}.log`), fd = openSync(log, 'w', 0o600);
  let r;
  try { r = spawnSync('docker', ['exec', ...(step === 'build' ? [] : ['--workdir', '/tmp/source']), name, ...args], { stdio: ['ignore', fd, fd], timeout: 30 * 60 * 1000 }); }
  finally { closeSync(fd); }
  if (r.status !== 0 || r.error) throw new Error(`${step} failed: ${r.error?.message ?? r.status}; original log ${log}`);
}
try {
  docker(['run', '-d', '--name', name, '--label', `strataai.local.source=${label}`,
    '--mount', `type=bind,source=${root},target=/candidate,readonly`,
    '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '-e', 'MSBUILDDISABLENODEREUSE=1',
    'mcr.microsoft.com/dotnet/sdk:10.0', 'sleep', 'infinity']);
  created = true;
  atomicJson(join(evidence, 'inputs.json'), { tree, name, label, platform: 'local Linux SDK', qualification: 'Complete API method coverage in bounded processes; original hosted single-process qualification remains required.' });
  run('build', ['bash', '-ec', `mkdir /tmp/source /tmp/api-results
cp -a /candidate/. /tmp/source/
cd /tmp/source
dotnet restore tests/StrataAI.Api.Tests/StrataAI.Api.Tests.csproj --locked-mode
dotnet build tests/StrataAI.Api.Tests/StrataAI.Api.Tests.csproj -c Release --no-restore -warnaserror -p:UseSharedCompilation=false -nodeReuse:false
dotnet build-server shutdown
`]);
  const discovery = docker(['exec', '--workdir', '/tmp/source', name, executable, '-list', 'methods/json', '-noLogo', '-noColor']);
  writeFileSync(join(evidence, 'discovery.json'), discovery + '\n', { mode: 0o600 });
  const methods = JSON.parse(discovery);
  if (!Array.isArray(methods) || !methods.length || new Set(methods).size !== methods.length || methods.some(m => typeof m !== 'string' || !/^StrataAI\.Api\.Tests\.[A-Za-z0-9_+.]+$/.test(m))) throw new Error('Invalid or incomplete API method discovery');
  const batches = [];
  for (let i = 0; i < methods.length; i += 20) batches.push(methods.slice(i, i + 20));
  atomicJson(join(evidence, 'inventory.json'), { methods, batches });
  for (const [i, batch] of batches.entries()) {
    const id = String(i).padStart(3, '0');
    run(`batch-${id}`, [executable, '-noLogo', '-noColor', '-parallelMode', 'none', ...batch.flatMap(m => ['-method', m]), '-result-xml', `/tmp/api-results/batch-${id}.xml`]);
  }
  docker(['cp', `${name}:/tmp/api-results`, join(evidence, 'xml')]); copied = true;
  const check = spawnSync('python3', ['scripts/local/api-test-results.py', join(evidence, 'inventory.json'), join(evidence, 'xml'), join(evidence, 'summary.json')], { stdio: 'inherit', timeout: 30_000 });
  if (check.status !== 0 || check.error) throw new Error('API discovery/result completeness validation failed');
  console.log(readFileSync(join(evidence, 'summary.json'), 'utf8'));
} finally {
  if (created) {
    if (!copied) { try { docker(['cp', `${name}:/tmp/api-results`, join(evidence, 'xml')]); } catch (error) { console.error(`Evidence copy failed: ${error.message}`); } }
    const actual = docker(['inspect', '--format', '{{index .Config.Labels "strataai.local.source"}}', name]);
    if (actual !== label) throw new Error('Refuse cleanup: source container ownership differs');
    docker(['rm', '-f', name]);
  }
  rmSync(root, { recursive: true, force: true });
}
