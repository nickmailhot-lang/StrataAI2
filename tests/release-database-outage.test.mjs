import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { basename, dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { copyFileSync, existsSync, mkdtempSync, mkdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import test from 'node:test';

const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
const source = readFileSync('scripts/ci/test-release-bundle-startup.sh', 'utf8');
const contract = source.match(/check_database_outage\(\) \{[\s\S]*?\n\}/)?.[0];
assert.ok(contract, 'Maintained database outage contract must be present');
const scenarios = ['healthy-recovery', 'stop-failed', 'bad-database-exit', 'api-liveness', 'worker-liveness',
  'api-stays-ready', 'worker-stays-ready', 'api-private-readiness', 'worker-private-readiness',
  'operator-accepts-outage', 'operator-unrelated-failure', 'database-start-failed', 'postgres-replaced',
  'volume-replaced', 'ledger-changed', 'host-wait-failed',
  ...['api', 'worker', 'web'].flatMap(host => [`${host}-replaced`, `${host}-restarted`])];
for (const scenario of scenarios) test(`database outage gate: ${scenario}`, { timeout: 15_000 }, async () => {
  const root = mkdtempSync(join(tmpdir(), 'strataai-database-outage-')); let timer;
  const stopped = () => existsSync(join(root, 'stopped')) && !existsSync(join(root, 'restored'));
  const server = createServer((request, response) => {
    const host = request.url.startsWith('/worker/') ? 'worker' : 'api';
    let body = { status: 'ok', service: `strataai-${host}` };
    if (host === 'worker') body.timestamp = '2026-10-15T00:00:00Z';
    if (request.url.endsWith('/readyz') && stopped()) {
      response.statusCode = scenario === `${host}-stays-ready` ? 200 : 503;
      body = host === 'api' ? { status: 'not-ready', code: 'runtime_not_ready',
        message: 'The service is not ready. Try again later.', correlationId: 'safe-test-reference' } : { status: 'not-ready' };
      if (scenario === `${host}-private-readiness`) body.database = 'PRIVATE_DATABASE_DETAILS';
    }
    if (request.url.endsWith('/healthz') && stopped() && scenario === `${host}-liveness`) response.statusCode = 500;
    if (/build-metadata\.json$|\/runtime$/.test(request.url)) body = { revision: 'a'.repeat(40), version: '1.0.0' };
    response.end(JSON.stringify(body));
  });
  try {
    await new Promise(done => server.listen(0, '127.0.0.1', done));
    const origin = `http://127.0.0.1:${server.address().port}`;
    mkdirSync(join(root, 'scratch'));
    writeFileSync(join(root, 'build-metadata.json'), JSON.stringify({ commitSha: 'a'.repeat(40), version: '1.0.0' }));
    for (const name of ['verify-release-runtime.sh', 'health-check.sh']) copyFileSync(resolve('scripts', name), join(root, name));
    if (scenario.startsWith('operator-')) writeFileSync(join(root, 'verify-release-runtime.sh'),
      scenario === 'operator-accepts-outage' ? 'exit 0\n' : 'echo unrelated-failure >&2\nexit 1\n');
    writeFileSync(join(root, 'driver.sh'), `#!/usr/bin/env bash
set -euo pipefail
compose=(docker compose)
runtime_file=runtime-response.json
POSTGRES_USER=fixture_admin
POSTGRES_DB=fixture_database
docker() {
  printf '%s\\n' "$*" >> calls.log
  if [ "$1" = inspect ]; then
    case "$3" in
      '{{.State.Running}}') printf false ;;
      '{{.State.ExitCode}}') if [ "$SCENARIO" = bad-database-exit ]; then printf 137; else printf 0; fi ;;
      '{{.State.StartedAt}}')
        if [ -f restored ] && [ "$SCENARIO" = "${'${4%-original}'}-restarted" ]; then printf changed-start; else printf original-start; fi ;;
      *) if [ -f restored ] && [ "$SCENARIO" = volume-replaced ]; then printf new-volume; else printf original-volume; fi ;;
    esac
    return
  fi
  test "$1" = compose; shift
  case "$1" in
    ps) if [ -f restored ] && [ "$SCENARIO" = "$3-replaced" ]; then printf '%s-new' "$3"; else printf '%s-original' "$3"; fi ;;
    exec) if [ -f restored ] && [ "$SCENARIO" = ledger-changed ]; then printf '${'b'.repeat(32)}'; else printf '${'a'.repeat(32)}'; fi ;;
    stop) test "$*" = 'stop --timeout 20 postgres'; test "$SCENARIO" != stop-failed; touch stopped ;;
    up)
      case "${'${*: -1}'}" in
        postgres) test "$SCENARIO" != database-start-failed; touch restored ;;
        web) test "$SCENARIO" != host-wait-failed; touch hosts-waited ;;
        *) return 64 ;;
      esac ;;
    *) return 64 ;;
  esac
}
check_runtime() { test -f restored; test -f hosts-waited; printf verified > recovered.checked; }
${contract}
check_database_outage
`);
    const child = spawn(bash, ['driver.sh'], { cwd: root, env: { ...process.env, BASH_ENV: undefined, SCENARIO: scenario,
      STRATAAI_API_URL: `${origin}/api`, STRATAAI_WORKER_URL: `${origin}/worker`, STRATAAI_WEB_URL: `${origin}/web`,
      STRATAAI_HEALTH_TIMEOUT_SECONDS: '1', TMPDIR: join(root, 'scratch') } });
    let output = '', overdue = false;
    child.stdout.on('data', data => { output += data; }); child.stderr.on('data', data => { output += data; });
    timer = setTimeout(() => { overdue = true; server.closeAllConnections(); child.kill('SIGKILL'); }, 10_000);
    const code = await new Promise((done, reject) => { child.once('error', reject); child.once('close', done); });
    clearTimeout(timer); assert.equal(overdue, false); assert.equal(output.includes('PRIVATE_DATABASE_DETAILS'), false);
    if (scenario === 'healthy-recovery') {
      assert.equal(code, 0, output); assert.match(output, /Database outage passed:/);
      assert.equal(readFileSync(join(root, 'recovered.checked'), 'utf8'), 'verified');
      const calls = readFileSync(join(root, 'calls.log'), 'utf8');
      assert.ok(calls.includes('up -d --no-deps --no-recreate --no-build --pull never --wait --wait-timeout 120 postgres'));
      assert.ok(calls.includes('up -d --no-deps --no-recreate --no-build --pull never --wait --wait-timeout 120 api worker web'));
    } else { assert.notEqual(code, 0, output); assert.doesNotMatch(output, /Database outage passed:/); }
  } finally {
    clearTimeout(timer); server.closeAllConnections(); await new Promise(done => server.close(done));
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir())); assert.match(basename(root), /^strataai-database-outage-/);
    rmSync(root, { recursive: true, force: true });
  }
});
