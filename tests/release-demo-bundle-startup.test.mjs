import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, mkdirSync, readFileSync, rmSync, realpathSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname, basename } from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const script = resolve('scripts/ci/test-release-demo-bundle-startup.sh');
const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
function fixture(check) {
  const root = mkdtempSync(join(tmpdir(), 'strataai-demo-bundle-test-'));
  try {
    writeFileSync(join(root, 'build-metadata.json'), JSON.stringify({ commitSha: 'a'.repeat(40), version: 'test-version' }));
    writeFileSync(join(root, 'compose.demo.release.yml'), 'services: {}\n');
    check(root);
  } finally {
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
    assert.match(basename(root), /^strataai-demo-bundle-test-/);
    rmSync(root, { recursive: true, force: true });
  }
}
for (const existing of ['.env.demo', '.git', 'src', 'apps']) test(`Demo bundle refuses existing ${existing} before external commands`, () => {
  fixture(root => {
    if (existing === '.env.demo') writeFileSync(join(root, existing), 'operator-settings\n');
    else mkdirSync(join(root, existing));
    const result = spawnSync(bash, [script, root], { encoding: 'utf8', env: { ...process.env, PATH: root, BASH_ENV: undefined } });
    assert.equal(result.status, 1, result.stderr);
    if (existing === '.env.demo') assert.equal(readFileSync(join(root, existing), 'utf8'), 'operator-settings\n');
  });
});

for (const scenario of ['healthy', 'startup-failure', 'redirect', 'internal-redirect', 'internal-transport-failure', 'unhealthy', 'wrong-image', 'image-inspection-failure', 'wrong-mode', 'bad-login', 'wrong-account', 'volume-created', 'startup-volume-inventory-failure', 'cleanup-failure', 'inventory-failure', 'leftover-network']) {
  test(`Demo bundle gate: ${scenario}`, () => fixture(root => {
    const tools = join(root, 'fixture-tools.sh');
    const jq = join(root, 'jq-fixture.mjs');
    writeFileSync(jq, `import { readFileSync } from 'node:fs';
const args=process.argv.slice(2); const data=JSON.parse(readFileSync(args.at(-1),'utf8'));
if(args[0]==='-er'){const value=data[args[1].slice(1)]; if(typeof value!=='string')process.exit(1); console.log(value);}
else {const expression=args.at(-2); let ok=true;
if(expression.includes('.mode'))ok=data.mode==='demo';
if(expression.includes('.revision'))ok=ok&&data.revision===args[args.indexOf('revision')+1]&&data.version===args[args.indexOf('version')+1];
if(expression.includes('.user.email'))ok=ok&&data.user?.email==='demo@strataai.test';
else if(expression.includes('.email'))ok=ok&&data.email==='demo@strataai.test';
process.exit(ok?0:1);}
`);
    writeFileSync(tools, `jq() { "$STRATAAI_TEST_NODE" "$STRATAAI_TEST_JQ" "$@"; }
openssl() { printf aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; }
docker() {
  printf '%s\\n' "$*" >> "$STRATAAI_TEST_ROOT/docker.calls"
  if [ "$1" = inspect ]; then
    if [ "$3" = '{{.State.Health.Status}}' ]; then
      if [ "$STRATAAI_TEST_CASE" = unhealthy ]; then printf unhealthy; else printf healthy; fi
      return 0
    fi
    if [ "$STRATAAI_TEST_CASE" = image-inspection-failure ]; then return 8; fi
    local host="\${!#}"; host="\${host#container-}"
    if [ "$STRATAAI_TEST_CASE" = wrong-image ] && [ "$host" = api ]; then printf wrong-image; else printf 'image-%s' "$host"; fi
  elif [ "$1" = image ]; then
    if [ "$STRATAAI_TEST_CASE" = image-inspection-failure ]; then return 8; fi
    local host="\${!#}"; host="\${host#strataai-}"; printf 'image-%s' "\${host%%:*}"
  elif [ "$1" = compose ]; then
    case " $* " in
      *' config --services '*) printf 'api\\nworker\\nweb\\n' ;;
      *' up '*)
        [[ " $* " == *' --no-build --pull never --wait --wait-timeout 120 '* ]] || return 64
        if [ "$STRATAAI_TEST_CASE" = startup-failure ]; then return 9; fi ;;
      *' ps -q '*) printf 'container-%s' "\${!#}" ;;
      *' port web '*) printf '127.0.0.1:1000' ;;
      *' port api '*) printf '127.0.0.1:1001' ;;
      *' port worker '*) printf '127.0.0.1:1002' ;;
      *' exec -T '*)
        if [ "$STRATAAI_TEST_CASE" = internal-transport-failure ]; then return 28; fi
        local mode=demo
        if [ "$STRATAAI_TEST_CASE" = wrong-mode ]; then mode=production; fi
        local status=200
        if [ "$STRATAAI_TEST_CASE" = internal-redirect ]; then status=302; fi
        printf '{"mode":"%s","revision":"%s","version":"test-version"}\\n%s' "$mode" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa "$status" ;;
      *' down '*)
        printf cleaned > "$STRATAAI_TEST_ROOT/cleanup.called"
        if [ "$STRATAAI_TEST_CASE" = cleanup-failure ]; then return 7; fi ;;
      *) return 64 ;;
    esac
  elif [ "$1" = ps ] || [ "$1" = volume ] || [ "$1" = network ]; then
    if [ "$STRATAAI_TEST_CASE" = startup-volume-inventory-failure ] && [ "$1" = volume ] && [ ! -f "$STRATAAI_TEST_ROOT/volume.checked" ]; then
      printf checked > "$STRATAAI_TEST_ROOT/volume.checked"; return 8
    fi
    if [ "$STRATAAI_TEST_CASE" = inventory-failure ]; then return 8; fi
    if [ "$STRATAAI_TEST_CASE" = leftover-network ] && [ "$1" = network ]; then printf leftover; fi
    if [ "$STRATAAI_TEST_CASE" = volume-created ] && [ "$1" = volume ]; then printf unexpected-volume; fi
  else return 64
  fi
}
curl() {
  local output='' url="\${!#}" status=200 retry_key=''
  while [ "$#" -gt 0 ]; do
    if [ "$1" = -o ]; then shift; output=$1; fi
    if [ "$1" = -H ]; then
      shift
      if [[ "$1" == 'Idempotency-Key: '* ]]; then retry_key="\${1#Idempotency-Key: }"; fi
    fi
    shift
  done
  if [[ "$url" == */auth/login ]]; then
    [[ "$retry_key" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]] || return 64
  fi
  case "$url" in
    */auth/login) printf '{"user":{"email":"demo@strataai.test"}}' > "$output" ;;
    */login) printf '<div id="root"></div>' > "$output" ;;
    */me) if [ "$STRATAAI_TEST_CASE" = wrong-account ]; then printf '{"email":"other@example.test"}' > "$output"; else printf '{"email":"demo@strataai.test"}' > "$output"; fi ;;
    *)
      local mode=demo
      if [ "$STRATAAI_TEST_CASE" = wrong-mode ]; then mode=production; fi
      printf '{"mode":"%s","revision":"%s","version":"test-version"}' "$mode" aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa > "$output" ;;
  esac
  if [ "$STRATAAI_TEST_CASE" = redirect ] && [[ "$url" == */readyz ]]; then status=302; fi
  if [ "$STRATAAI_TEST_CASE" = bad-login ] && [[ "$url" == */auth/login ]]; then status=401; fi
  printf '%s' "$status"
}
`);
    const result = spawnSync(bash, [script, root], { encoding: 'utf8', env: { ...process.env,
      BASH_ENV: tools, STRATAAI_TEST_NODE: process.execPath.replaceAll('\\', '/'),
      STRATAAI_TEST_JQ: jq.replaceAll('\\', '/'), STRATAAI_TEST_ROOT: root.replaceAll('\\', '/'), STRATAAI_TEST_CASE: scenario } });
    assert.equal(result.status === 0, scenario === 'healthy', result.stderr || result.stdout);
    assert.equal(readFileSync(join(root, 'cleanup.called'), 'utf8'), 'cleaned');
    assert.throws(() => readFileSync(join(root, '.env.demo')), { code: 'ENOENT' });
    const calls = readFileSync(join(root, 'docker.calls'), 'utf8');
    assert.ok(!calls.includes(' build '));
    if (scenario === 'healthy') assert.match(result.stdout, /seeded sign-in passed/);
  }));
}
