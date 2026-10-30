import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtempSync, mkdirSync, readdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import test from 'node:test';

import { testBash as bash } from './local-test-tools.mjs';
const source = readFileSync('scripts/ci/test-work-events.sh', 'utf8').replace(/\r\n/g, '\n');
const end = source.indexOf('\nrequest() {');
assert.ok(end > 0, 'Execute the actual driver through registration/login, before domain writes');
const privateBody = 'private-account-password-cookie-response';

async function fixture(scenario = {}) {
  const root = mkdtempSync(join(tmpdir(), 'strataai-work-admission-'));
  mkdirSync(join(root, 'scratch'));
  // A local curl configuration must not add redirects/retries to CI admission.
  writeFileSync(join(root, '.curlrc'), 'location\n');
  const script = join(root, 'admission.sh');
  writeFileSync(script, `docker() { return 0; }\n${source.slice(0, end)}\nprintf 'ADMISSION_COMPLETED\\n'\n`);
  const requests = [];
  const server = createServer((request, response) => {
    requests.push(request.url);
    const selected = request.url === (scenario.path ?? '/auth/register');
    if (selected && scenario.stall) return;
    response.statusCode = selected ? (scenario.status ?? 201) : (request.url === '/auth/register' ? 201 : 200);
    response.setHeader('Set-Cookie', `session=${privateBody}; HttpOnly`);
    if (selected && scenario.status === 302) response.setHeader('Location', '/redirect-target');
    response.end(selected && scenario.body !== undefined ? scenario.body : JSON.stringify({ user: { id: '11111111-1111-4111-8111-111111111111' }, privateBody }));
  });
  let child, timer;
  try {
    await new Promise(done => server.listen(0, '127.0.0.1', done));
    child = spawn(bash, [script, `http://127.0.0.1:${server.address().port}`], {
      env: { ...process.env, CI: 'true', BASH_ENV: undefined, CURL_HOME: root, TMPDIR: join(root, 'scratch') },
    });
    let output = '', overdue = false;
    child.stdout.on('data', data => { output += data; });
    child.stderr.on('data', data => { output += data; });
    timer = setTimeout(() => { overdue = true; server.closeAllConnections(); child.kill('SIGKILL'); }, scenario.stall ? 34000 : 8000);
    const code = await new Promise((done, reject) => { child.once('error', reject); child.once('close', done); });
    clearTimeout(timer);
    assert.equal(overdue, false, 'The request deadline must fire before the fixture watchdog');
    assert.equal(output.includes(privateBody), false, 'No response, cookie, account or credential data in diagnostics');
    assert.deepEqual(readdirSync(join(root, 'scratch')), [], 'EXIT must retire private response/cookie files');
    return { code, output, requests };
  } finally {
    clearTimeout(timer);
    server.closeAllConnections(); await new Promise(done => server.close(done));
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
    assert.match(basename(root), /^strataai-work-admission-/);
    rmSync(root, { recursive: true, force: true });
  }
}

test('real driver admits one 201 registration followed by one 200 login', async () => {
  const r = await fixture(); assert.equal(r.code, 0, r.output);
  assert.match(r.output, /ADMISSION_COMPLETED/);
  assert.deepEqual(r.requests, ['/auth/register', '/auth/login']);
});

for (const [path, status, category] of [
  ['/auth/register', 503, 'identity_storage_unavailable'],
  ['/auth/register', 403, 'self_registration_disabled'],
  ['/auth/register', 429, 'rate_limited'],
  ['/auth/login', 403, 'email_verification_required'],
  ['/auth/login', 401, 'invalid_credentials'],
]) test(`${path} ${status} retains only the allowlisted failure category without retry`, async () => {
  const r = await fixture({ path, status, body: JSON.stringify({ code: category, detail: privateBody, correlationId: privateBody }) });
  assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /ADMISSION_COMPLETED/);
  assert.match(r.output, new RegExp(`status=${status} category=${category}`));
  assert.deepEqual(r.requests, path === '/auth/login' ? ['/auth/register', path] : [path]);
});

for (const body of [privateBody, JSON.stringify({ code: privateBody }), JSON.stringify({ code: { privateBody } }), '[]', '{}', '{"code":"identity_storage_unavailable"}\n{}'])
  test(`untrusted error shape ${body.slice(0, 24)} has a fixed private diagnostic`, async () => {
    const r = await fixture({ status: 503, body });
    assert.notEqual(r.code, 0); assert.match(r.output, /status=503 category=unrecognized_response/);
    assert.deepEqual(r.requests, ['/auth/register']);
  });

for (const [path, status] of [['/auth/register', 200], ['/auth/register', 204], ['/auth/login', 201], ['/auth/login', 204], ['/auth/register', 302]])
  test(`${path} unexpected ${status} cannot admit domain work or follow redirects`, async () => {
    const r = await fixture({ path, status });
    assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /ADMISSION_COMPLETED/);
    assert.deepEqual(r.requests, path === '/auth/login' ? ['/auth/register', path] : [path]);
  });

test('an oversized admission response is refused without logging it or retrying', async () => {
  const r = await fixture({ body: ' '.repeat(100000) + privateBody });
  assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /ADMISSION_COMPLETED/);
  assert.deepEqual(r.requests, ['/auth/register']);
});

test('a withheld registration response reaches the actual 30-second deadline without retry', async () => {
  const r = await fixture({ stall: true });
  assert.notEqual(r.code, 0); assert.match(r.output, /registration transport_or_size_failure/);
  assert.deepEqual(r.requests, ['/auth/register']);
});
