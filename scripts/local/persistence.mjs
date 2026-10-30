// Linux execution on every host preserves production-only file capability checks.
import { execFileSync, spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { context, snapshot, materialize } from './repository.mjs';
import { rmSync, readFileSync } from 'node:fs';
const repository = context().root;
const root = materialize(snapshot(repository));
const name = `strataai-contract-${randomUUID().replaceAll('-', '')}`;
const label = randomUUID(); let id; let passed = false;
const docker = args => execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'], maxBuffer: 32 * 1024 * 1024, timeout: 30 * 60 * 1000 }).trim();
try {
  id = docker(['run', '-d', '--name', name, '--label', `strataai.local.contract=${label}`, '-e', 'POSTGRES_DB=strataai_ci', '-e', 'POSTGRES_PASSWORD=postgres',
    '--mount', `type=bind,source=${root}/db,target=/workspace/db,readonly`, 'pgvector/pgvector:pg17']);
  let ready = false;
  for (let i = 0; i < 60; i++) {
    if (spawnSync('docker', ['exec', id, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres', '-d', 'strataai_ci'], { stdio: 'ignore' }).status === 0) { ready = true; break; }
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 1000);
  }
  if (!ready) throw new Error('Disposable PostgreSQL readiness failed.');
  const migrations = execFileSync('bash', ['scripts/migration-stream.sh'], { cwd: root, maxBuffer: 32 * 1024 * 1024 });
  execFileSync('docker', ['exec', '-i', id, 'psql', '-X', '-U', 'postgres', '-d', 'strataai_ci', '-v', 'ON_ERROR_STOP=1'], { input: migrations, maxBuffer: 32 * 1024 * 1024 });
  docker(['exec', '-e', 'STRATAAI_API_DB_PASSWORD=ci-api-runtime-password', '-e', 'STRATAAI_WORKER_DB_PASSWORD=ci-worker-runtime-password', id,
    'psql', '-X', '-U', 'postgres', '-d', 'strataai_ci', '-v', 'ON_ERROR_STOP=1', '-f', '/workspace/db/provision-runtime-roles.sql']);
  execFileSync('docker', ['exec', '-i', id, 'psql', '-X', '-U', 'postgres', '-d', 'strataai_ci', '-v', 'ON_ERROR_STOP=1'], { input: readFileSync(`${root}/scripts/ci/grant-identity-worker.sql`) });
  const result = spawnSync('docker', ['run', '--rm', '--network', `container:${id}`, '--mount', `type=bind,source=${root},target=/workspace,readonly`,
    ...[['ADMIN','postgres','postgres'], ['API','strataai_api_runtime','ci-api-runtime-password'], ['WORKER','strataai_worker_runtime','ci-worker-runtime-password'], ['IDENTITY','strataai_identity_mail_ci','identity-ci-password']].flatMap(([kind,user,password]) =>
      ['-e', `STRATAAI_CONTRACT_${kind}_CONNECTION=Host=127.0.0.1;Port=5432;Database=strataai_ci;Username=${user};Password=${password};SSL Mode=Disable`]),
    'mcr.microsoft.com/dotnet/sdk:10.0', 'bash', '-ec', `mkdir /tmp/contracts
cd /workspace
tar --exclude=bin --exclude=obj -cf - global.json Directory.Build.props Directory.Build.targets Directory.Packages.props src scripts/ci tests/StrataAI.Persistence.Contracts | tar -C /tmp/contracts -xf -
cd /tmp/contracts
dotnet restore tests/StrataAI.Persistence.Contracts/StrataAI.Persistence.Contracts.csproj --locked-mode
dotnet build tests/StrataAI.Persistence.Contracts/StrataAI.Persistence.Contracts.csproj -c Release --no-restore -warnaserror
dotnet run --project tests/StrataAI.Persistence.Contracts/StrataAI.Persistence.Contracts.csproj -c Release --no-build`], { stdio: 'inherit', timeout: 60 * 60 * 1000 });
  if (result.status !== 0 || result.error) throw new Error('Restricted persistence contracts failed.');
  passed = true;
} finally {
  rmSync(root, { recursive: true, force: true });
  if (id) {
    if (passed && docker(['inspect', '--format', '{{index .Config.Labels "strataai.local.contract"}}', id]) === label) docker(['rm', '-fv', id]);
    else console.error(`Failed contract database retained for inspection: ${name}`);
  }
}
