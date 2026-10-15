import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import test from 'node:test';

const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
const script = resolve('scripts/ci/test-release-bundle-startup.sh');
function fixture(check) {
  const root = mkdtempSync(join(tmpdir(), 'strataai-startup-refusal-'));
  try {
    writeFileSync(join(root, 'build-metadata.json'), '{}');
    check(root, () => spawnSync(bash, [script, root], { encoding: 'utf8',
      env: { ...process.env, PATH: root, BASH_ENV: undefined } }));
  } finally {
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
    assert.match(basename(root), /^strataai-startup-refusal-/);
    rmSync(root, { recursive: true, force: true });
  }
}

test('startup refuses existing operator settings without rewriting or requiring any external command', () => {
  fixture((root, run) => {
    const original = 'PUBLIC_TEST_FIXTURE=operator-settings-must-survive\n';
    writeFileSync(join(root, '.env'), original);
    const result = run();
    assert.equal(result.status, 1);
    assert.equal(result.stdout, '');
    assert.equal(result.stderr, '');
    assert.equal(readFileSync(join(root, '.env'), 'utf8'), original);
  });
});

const startupSource = readFileSync(script, 'utf8');
const runtimeCheck = startupSource.match(/check_runtime\(\) \{[\s\S]*?\n\}/)?.[0];
assert.ok(runtimeCheck, 'Maintained runtime identity check must be present');
const runtimePaths = ['/build-metadata.json', '/api/runtime', '/runtime'];
const runtimeCases = [
  { name: 'matching exact identities' },
  ...runtimePaths.flatMap(path => [301, 302, 307].map(status => ({ name: `${path} redirect ${status}`, path, status }))),
  ...runtimePaths.map(path => ({ name: `${path} unavailable`, path, status: 503 })),
  { name: 'wrong revision', body: { revision: 'b'.repeat(40), version: '1.0.0' } },
  { name: 'wrong version', body: { revision: 'a'.repeat(40), version: '2.0.0' } },
  { name: 'malformed metadata', malformed: true },
  { name: 'stalled metadata response', stalled: true },
];
for (const scenario of runtimeCases) test(`release runtime metadata: ${scenario.name}`, async () => {
  const root = mkdtempSync(join(tmpdir(), 'strataai-runtime-metadata-'));
  const server = createServer((request, response) => {
    if (scenario.stalled) {
      // Bound the server side too, so a missing client deadline cannot leave
      // an orphaned curl holding the Bash child's output pipes on Windows.
      request.socket.setTimeout(22_000, () => request.socket.destroy());
      return;
    }
    response.statusCode = request.url === scenario.path ? scenario.status : 200;
    response.setHeader('Content-Type', 'application/json');
    if (response.statusCode >= 300 && response.statusCode < 400) response.setHeader('Location', '/redirect-target');
    response.end(scenario.malformed ? '{' : JSON.stringify(scenario.body ?? { revision: 'a'.repeat(40), version: '1.0.0' }));
  });
  let child;
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    // Health refusal behavior has its own real-HTTP suite. Isolate this
    // identity boundary after a successful preceding health gate.
    writeFileSync(join(root, 'health-check.sh'), '#!/usr/bin/env bash\nexit 0\n');
    const driver = join(root, 'runtime-test.sh');
    writeFileSync(driver, `#!/usr/bin/env bash
set -euo pipefail
compose=(docker compose)
revision=${'a'.repeat(40)}
version=1.0.0
runtime_file=runtime-response.json
declare -A loaded_image_ids=([web]=web-image [api]=api-image [worker]=worker-image)
docker() {
  if [ "$1" = compose ] && [ "$2" = ps ]; then printf '%s-container' "$4"; return 0; fi
  if [ "$1" = inspect ] && [ "$3" = '{{.Image}}' ]; then printf '%s-image' "\${4%-container}"; return 0; fi
  return 64
}
curl() {
  local value; local args=()
  for value in "$@"; do
    value="\${value/127.0.0.1:8088/127.0.0.1:${port}}"
    value="\${value/127.0.0.1:8080/127.0.0.1:${port}}"
    value="\${value/127.0.0.1:8081/127.0.0.1:${port}}"
    args+=("$value")
  done
  command curl "\${args[@]}"
}
${runtimeCheck}
check_runtime
printf verified > runtime.checked
`);
    let stdout = '', stderr = '';
    const result = await new Promise((resolveResult, reject) => {
      child = spawn(bash, [driver], { cwd: root, env: { ...process.env, BASH_ENV: undefined }, timeout: 25_000 });
      child.stdout.on('data', chunk => { stdout += chunk; });
      child.stderr.on('data', chunk => { stderr += chunk; });
      child.on('error', reject);
      child.on('close', (code, signal) => resolveResult({ code, signal }));
    });
    assert.equal(result.signal, null, 'Runtime request must have its own deadline before the outer fixture timeout');
    assert.equal(child.killed, false, 'The outer fixture timeout must not substitute for a runtime request deadline');
    if (scenario.stalled) assert.equal(result.code, 28, 'Stalled metadata must stop at curl\'s own deadline');
    if (scenario.name === 'matching exact identities') {
      assert.equal(result.code, 0, stderr);
      assert.equal(readFileSync(join(root, 'runtime.checked'), 'utf8'), 'verified');
    } else {
      assert.notEqual(result.code, 0, 'Runtime metadata refusals must prevent promotion');
      assert.throws(() => readFileSync(join(root, 'runtime.checked')), { code: 'ENOENT' });
    }
    assert.equal(stdout, '');
  } finally {
    child?.kill();
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
    assert.match(basename(root), /^strataai-runtime-metadata-/);
    rmSync(root, { recursive: true, force: true });
  }
});

const restartStart = startupSource.indexOf('\napi=$(');
const restartEnd = startupSource.indexOf('\n# ARCH-12-AC-003:');
assert.ok(restartStart >= 0 && restartEnd > restartStart, 'Maintained restart sequence must be present');
const restartSequence = startupSource.slice(restartStart + 1, restartEnd);
for (const scenario of [
  { name: 'portable Compose restart keeps existing containers', status: 0 },
  { name: 'API exit failure', badExit: 'api-original', status: 1 },
  { name: 'Worker exit failure', badExit: 'worker-original', status: 1 },
  { name: 'API OOM', badOom: 'api-original', status: 1 },
  { name: 'Worker OOM', badOom: 'worker-original', status: 1 },
  { name: 'API replacement', replacement: 'api', status: 1 },
  { name: 'Worker replacement', replacement: 'worker', status: 1 },
  { name: 'restart readiness failure', waitFailure: true, status: 9 },
  { name: 'post-restart runtime failure', healthFailure: true, status: 12 },
]) test(`release restart: ${scenario.name}`, () => {
  fixture(root => {
    const driver = join(root, 'restart-test.sh');
    writeFileSync(driver, `#!/usr/bin/env bash
set -euo pipefail
compose=(docker compose)
docker() {
  printf '%s\\n' "$*" >> calls.log
  if [ "$1" = inspect ]; then
    case "$3" in
      '{{.State.ExitCode}}') if [ "$STRATAAI_TEST_BAD_EXIT" = "$4" ]; then printf 143; else printf 0; fi ;;
      '{{.State.OOMKilled}}') if [ "$STRATAAI_TEST_BAD_OOM" = "$4" ]; then printf true; else printf false; fi ;;
      *) return 64 ;;
    esac
    return 0
  fi
  if [ "$1" != compose ]; then return 64; fi
  case "$2" in
    ps) if [ -f restarted ] && [ "$STRATAAI_TEST_REPLACEMENT" = "$4" ]; then printf '%s-replacement' "$4"; else printf '%s-original' "$4"; fi ;;
    stop) test "$*" = 'compose stop --timeout 20 api worker' ;;
    start) printf 'unknown flag: --wait\\n' >&2; return 64 ;;
    up)
      test "$*" = 'compose up --no-deps --no-recreate --wait --wait-timeout 120 api worker'
      if [ "$STRATAAI_TEST_WAIT_FAILURE" = 1 ]; then return 9; fi
      printf restarted > restarted ;;
    *) return 64 ;;
  esac
}
check_runtime() {
  if [ "$STRATAAI_TEST_HEALTH_FAILURE" = 1 ]; then return 12; fi
  printf verified > runtime.checked
}
${restartSequence}
`);
    const result = spawnSync(bash, [driver], { cwd: root, encoding: 'utf8', env: { ...process.env, BASH_ENV: undefined,
      STRATAAI_TEST_BAD_EXIT: scenario.badExit ?? '', STRATAAI_TEST_BAD_OOM: scenario.badOom ?? '',
      STRATAAI_TEST_REPLACEMENT: scenario.replacement ?? '',
      STRATAAI_TEST_WAIT_FAILURE: scenario.waitFailure ? '1' : '0', STRATAAI_TEST_HEALTH_FAILURE: scenario.healthFailure ? '1' : '0' } });
    assert.equal(result.status, scenario.status, result.stderr);
    if (scenario.status === 0) {
      assert.equal(readFileSync(join(root, 'runtime.checked'), 'utf8'), 'verified');
      assert.ok(readFileSync(join(root, 'calls.log'), 'utf8').includes('compose up --no-deps --no-recreate --wait --wait-timeout 120 api worker'));
    } else assert.throws(() => readFileSync(join(root, 'runtime.checked')), { code: 'ENOENT' });
  });
});

const rotationEnd = startupSource.lastIndexOf('\necho ');
assert.ok(rotationEnd > restartEnd, 'Maintained password-rotation sequence must be present');
const rotationSequence = startupSource.slice(restartEnd + 1, rotationEnd);
for (const scenario of ['healthy', 'environment-write-failure', 'postgres-start-failure',
  'postgres-not-replaced', 'volume-changed', 'provision-failure', 'api-new-password-failure',
  'worker-new-password-failure', 'api-old-password-accepted', 'worker-old-password-accepted',
  'old-password-network-failure', 'ledger-changed', 'host-start-failure',
  'api-not-replaced', 'worker-not-replaced', 'runtime-failure']) {
  test(`release secret rotation: ${scenario}`, () => fixture(root => {
    writeFileSync(join(root, 'provision-runtime-roles.sh'), `#!/usr/bin/env bash
set -euo pipefail
test -f postgres.recreated
test -f environment.written
test "$STRATAAI_TEST_ROTATION" != provision-failure
printf provisioned > roles.provisioned
`);
    const driver = join(root, 'rotation-test.sh');
    writeFileSync(driver, `#!/usr/bin/env bash
set -euo pipefail
compose=(docker compose)
api=api-original; worker=worker-original
POSTGRES_USER=fixture_admin; POSTGRES_DB=fixture_db
STRATAAI_API_DB_PASSWORD=old-api-fixture
STRATAAI_WORKER_DB_PASSWORD=old-worker-fixture
openssl() {
  test "$*" = 'rand -hex 24'
  if [ -f random.first ]; then printf '%048d' 2; else printf '%048d' 1; touch random.first; fi
}
write_environment() {
  test "$STRATAAI_TEST_ROTATION" != environment-write-failure
  test "$STRATAAI_API_DB_PASSWORD" = 000000000000000000000000000000000000000000000001
  test "$STRATAAI_WORKER_DB_PASSWORD" = 000000000000000000000000000000000000000000000002
  touch environment.written
}
docker() {
  if [ "$1" = inspect ]; then
    if [ "$STRATAAI_TEST_ROTATION" = volume-changed ] && [ -f postgres.recreated ]; then printf other-volume; else printf original-volume; fi
    return 0
  fi
  test "$1" = compose
  shift
  case "$1" in
    ps)
      case "$3" in
        postgres) if [ -f postgres.recreated ] && [ "$STRATAAI_TEST_ROTATION" != postgres-not-replaced ]; then printf postgres-new; else printf postgres-original; fi ;;
        api|worker) if [ -f hosts.recreated ] && [ "$STRATAAI_TEST_ROTATION" != "$3-not-replaced" ]; then printf '%s-new' "$3"; else printf '%s-original' "$3"; fi ;;
        *) return 64 ;;
      esac ;;
    stop) test "$*" = 'stop --timeout 20 api worker' ;;
    up)
      case "$*" in
        'up -d --no-deps --force-recreate --wait --wait-timeout 120 postgres')
          test "$STRATAAI_TEST_ROTATION" != postgres-start-failure
          touch postgres.recreated ;;
        'up -d --no-deps --force-recreate --wait --wait-timeout 120 api worker')
          test -f roles.provisioned
          test "$STRATAAI_TEST_ROTATION" != host-start-failure
          touch hosts.recreated ;;
        *) return 64 ;;
      esac ;;
    exec)
      if [[ "$*" == *PGPASSWORD=* ]]; then
        [[ "$*" == *'postgres psql -h postgres -U '* ]] || return 64
      fi
      if [[ "$*" == *'PGPASSWORD=old-'* ]]; then
        if [[ "$*" == *PGPASSWORD=old-api-fixture* ]]; then role=api; else role=worker; fi
        if [ "$STRATAAI_TEST_ROTATION" = "$role-old-password-accepted" ]; then printf 1; return 0; fi
        if [ "$STRATAAI_TEST_ROTATION" = old-password-network-failure ]; then printf 'connection refused' >&2; return 2; fi
        printf 'password authentication failed' >&2; return 2
      fi
      if [[ "$*" == *PGPASSWORD=* ]]; then
        test -f roles.provisioned
        if [[ "$*" == *strataai_api_runtime* ]]; then role=api; else role=worker; fi
        test "$STRATAAI_TEST_ROTATION" != "$role-new-password-failure" || return 17
        if [ "$STRATAAI_TEST_ROTATION" = ledger-changed ]; then printf '%032d' 2; return 0; fi
      fi
      printf '%032d' 1 ;;
    *) return 64 ;;
  esac
}
check_runtime() {
  test -f hosts.recreated
  test "$STRATAAI_TEST_ROTATION" != runtime-failure
  printf verified > rotation.checked
}
${rotationSequence}
`);
    const result = spawnSync(bash, [driver], { cwd: root, encoding: 'utf8', timeout: 10_000,
      env: { ...process.env, BASH_ENV: undefined, GITHUB_ACTIONS: '', STRATAAI_TEST_ROTATION: scenario } });
    if (scenario === 'healthy') {
      assert.equal(result.status, 0, result.stderr);
      assert.equal(readFileSync(join(root, 'rotation.checked'), 'utf8'), 'verified');
    } else {
      assert.notEqual(result.status, 0);
      assert.throws(() => readFileSync(join(root, 'rotation.checked')), { code: 'ENOENT' });
    }
    assert.equal(result.stdout, '');
    assert.ok(!result.stderr.includes('old-api-fixture') && !result.stderr.includes('old-worker-fixture'));
  }));
}

test('startup refuses application checkouts before secret creation or Docker/SDK commands', () => {
  for (const source of ['.git', 'src', 'apps']) fixture((root, run) => {
    mkdirSync(join(root, source));
    const result = run();
    assert.equal(result.status, 1);
    assert.equal(result.stdout, '');
    assert.equal(result.stderr, '');
    assert.throws(() => readFileSync(join(root, '.env')), { code: 'ENOENT' });
  });
});

// Execute the maintained EXIT handler with a Docker stand-in. This exercises
// failure handling without creating containers or touching operator resources.
const cleanup = readFileSync(script, 'utf8').match(/cleanup\(\) \{[\s\S]*?\n\}/)?.[0];
assert.ok(cleanup, 'Maintained release cleanup function must be present');
for (const scenario of [
  { name: 'clean namespace', status: 0 },
  { name: 'upstream startup failure', upstream: 37, status: 37 },
  ...['container', 'volume', 'network'].map(leftover => ({ name: `leftover ${leftover}`, leftover, status: 1 })),
  ...['container', 'volume', 'network'].map(queryFailure => ({ name: `${queryFailure} query failure`, queryFailure, status: 1 })),
  { name: 'Compose teardown failure', downFailure: true, status: 1 },
]) test(`release cleanup fails closed: ${scenario.name}`, () => {
  fixture(root => {
    writeFileSync(join(root, '.env'), 'PRIVATE_TEST_SETTINGS=discard\n');
    writeFileSync(join(root, 'inspection.json'), 'PRIVATE_TEST_INSPECTION\n');
    writeFileSync(join(root, 'runtime.json'), 'PRIVATE_TEST_RUNTIME\n');
    const driver = join(root, 'cleanup-test.sh');
    writeFileSync(driver, `#!/usr/bin/env bash
set -euo pipefail
project=strataai-bundle-smoke-0123456789abcdef
compose=(docker compose)
inspection_file=inspection.json
runtime_file=runtime.json
docker() {
  printf '%s\\n' "$*" >> calls.log
  if [ "$1" = compose ]; then
    if [ "$STRATAAI_TEST_DOWN_FAILURE" = 1 ]; then return 9; fi
    return 0
  fi
  local task_kind=$1
  if [ "$task_kind" = ps ]; then task_kind=container; fi
  if [ "$task_kind" = "$STRATAAI_TEST_QUERY_FAILURE" ]; then return 9; fi
  if [ "$task_kind" = "$STRATAAI_TEST_LEFTOVER" ]; then printf '%s\\n' owned-resource; fi
  return 0
}
${cleanup}
trap cleanup EXIT
exit "$STRATAAI_TEST_UPSTREAM"
`);
    const result = spawnSync(bash, [driver], { cwd: root, encoding: 'utf8', env: { ...process.env, BASH_ENV: undefined,
      STRATAAI_TEST_DOWN_FAILURE: scenario.downFailure ? '1' : '0',
      STRATAAI_TEST_QUERY_FAILURE: scenario.queryFailure ?? '', STRATAAI_TEST_LEFTOVER: scenario.leftover ?? '',
      STRATAAI_TEST_UPSTREAM: String(scenario.upstream ?? 0) } });
    assert.equal(result.status, scenario.status, result.stderr);
    assert.equal(result.stdout, '');
    assert.throws(() => readFileSync(join(root, '.env')), { code: 'ENOENT' });
    assert.throws(() => readFileSync(join(root, 'inspection.json')), { code: 'ENOENT' });
    assert.throws(() => readFileSync(join(root, 'runtime.json')), { code: 'ENOENT' });
    const calls = readFileSync(join(root, 'calls.log'), 'utf8').trim().split('\n');
    assert.deepEqual(calls, [
      'compose down --volumes --remove-orphans',
      'ps -aq --filter label=com.docker.compose.project=strataai-bundle-smoke-0123456789abcdef',
      'volume ls -q --filter label=com.docker.compose.project=strataai-bundle-smoke-0123456789abcdef',
      'network ls -q --filter label=com.docker.compose.project=strataai-bundle-smoke-0123456789abcdef',
    ]);
  });
});
