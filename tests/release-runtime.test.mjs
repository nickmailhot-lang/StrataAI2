import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtempSync, mkdirSync, readdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import test from 'node:test';

const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
const script = resolve('scripts/verify-release-runtime.sh');
const expected = { commitSha: 'a'.repeat(40), version: '1.0.0' };
const identity = { revision: expected.commitSha, version: expected.version };
const paths = ['/web/build-metadata.json', '/api/api/runtime', '/worker/runtime', '/web/api/runtime'];
const privateBody = 'private-response-must-not-appear-in-diagnostics';
async function fixture(scenario = {}) {
  const root = mkdtempSync(join(tmpdir(), 'strataai-release-runtime-'));
  const requests = []; let child, timer;
  mkdirSync(join(root, 'scratch'));
  writeFileSync(join(root, 'expected.json'), scenario.manifest ?? JSON.stringify(expected));
  writeFileSync(join(root, '.curlrc'), 'location\ninsecure\n');
  writeFileSync(join(root, 'known-good.json'), 'prior-release-reference-must-survive');
  const server = createServer((request, response) => {
    requests.push(request.url);
    const selected = request.url === (scenario.path ?? paths[0]);
    if (selected && scenario.stall) return;
    response.statusCode = selected ? (scenario.status ?? 200) : 200;
    if (response.statusCode === 302) response.setHeader('Location', '/redirect-target');
    const runtime = [paths[1], paths[3]].includes(request.url) ? { service: 'strataai-api', mode: 'production' }
      : request.url === paths[2] ? { service: 'strataai-worker', mode: 'production' } : {};
    let body = JSON.stringify(paths.includes(request.url) ? { ...identity, ...runtime, privateBody } : { privateBody });
    if (selected && scenario.body !== undefined) body = scenario.body;
    if (selected && scenario.large) {
      body = ' '.repeat(5000) + JSON.stringify(identity);
      if (!scenario.chunked) response.setHeader('Content-Length', Buffer.byteLength(body));
      else response.writeHead(200, { 'Transfer-Encoding': 'chunked' });
    }
    if (selected && scenario.cut) {
      response.setHeader('Content-Length', 9000); response.write(JSON.stringify(identity));
      setTimeout(() => response.destroy(), 30); return;
    }
    response.end(body);
  });
  try {
    await new Promise(done => server.listen(0, '127.0.0.1', done));
    const origin = `http://127.0.0.1:${server.address().port}`;
    child = spawn(bash, [script, ...(scenario.noArgument ? [] : [join(root, 'expected.json')])], {
      env: { ...process.env, BASH_ENV: undefined, CURL_HOME: root,
        STRATAAI_WEB_URL: `${origin}/web`, STRATAAI_API_URL: `${origin}/api`, STRATAAI_WORKER_URL: `${origin}/worker`,
        STRATAAI_HEALTH_TIMEOUT_SECONDS: scenario.timeout ?? '1', TMPDIR: join(root, 'scratch') },
    });
    let output = '', overdue = false;
    child.stdout.on('data', data => { output += data; }); child.stderr.on('data', data => { output += data; });
    timer = setTimeout(() => { overdue = true; server.closeAllConnections(); child.kill('SIGKILL'); }, 8000);
    const code = await new Promise((done, reject) => { child.once('error', reject); child.once('close', done); });
    clearTimeout(timer); assert.equal(overdue, false, 'Client deadlines must precede the outer watchdog');
    assert.equal(output.includes(privateBody), false);
    assert.equal(readFileSync(join(root, 'known-good.json'), 'utf8'), 'prior-release-reference-must-survive');
    assert.deepEqual(readdirSync(join(root, 'scratch')), [], 'Bounded private response files must be removed');
    return { code, output, requests };
  } finally {
    clearTimeout(timer); server.closeAllConnections(); await new Promise(done => server.close(done));
    assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir()));
    assert.match(basename(root), /^strataai-release-runtime-/); rmSync(root, { recursive: true, force: true });
  }
}
test('healthy web/API/Worker and proxied API must all match the selected bundle', async () => {
  const r = await fixture(); assert.equal(r.code, 0, r.output); assert.match(r.output, /build identity verified/);
  assert.deepEqual(r.requests.slice(-4), paths); assert.equal(r.requests.length, 10);
});
for (const [path, service] of [[paths[1], 'strataai-api'], [paths[2], 'strataai-worker'], [paths[3], 'strataai-api']]) {
  for (const mode of ['demo', null, undefined, 'unknown', 'Production'])
    test(`${path}: runtime mode ${String(mode)} refuses Production acceptance`, async () => {
      const r = await fixture({ path, body: JSON.stringify({ ...identity, service, mode }) });
      assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /build identity verified/);
    });
  for (const actualService of [undefined, service === 'strataai-api' ? 'strataai-worker' : 'strataai-api'])
    test(`${path}: wrong or missing service ${String(actualService)} refuses acceptance`, async () => {
      const r = await fixture({ path, body: JSON.stringify({ ...identity, service: actualService, mode: 'production' }) });
      assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /build identity verified/);
    });
}
for (const status of [302, 503]) test(`unhealthy API (${status}) refuses before identity reads`, async () => {
  const r = await fixture({ path: '/api/api/health', status }); assert.notEqual(r.code, 0);
  assert.deepEqual(r.requests, ['/api/api/health']); assert.doesNotMatch(r.output, /build identity verified/);
});
for (const path of paths) for (const [name, options] of [
  ['redirect despite local curl configuration', { status: 302 }], ['unavailable', { status: 503 }],
  ['wrong revision', { body: JSON.stringify({ ...identity, revision: 'b'.repeat(40) }) }],
  ['wrong version', { body: JSON.stringify({ ...identity, version: '2.0.0' }) }],
  ['malformed JSON', { body: '{' }],
]) test(`${path}: ${name} refuses promotion`, async () => {
  const r = await fixture({ path, ...options }); assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /build identity verified/);
  assert.equal(r.requests.includes('/redirect-target'), false);
});
for (const [name, options] of [
  ['multiple JSON documents', { body: `${JSON.stringify(identity)}\n${JSON.stringify(identity)}` }],
  ['nonobject response', { body: JSON.stringify([identity]) }],
  ['oversize with content length', { large: true }], ['oversize chunked body', { large: true, chunked: true }],
  ['truncated HTTP body', { cut: true }], ['stalled metadata', { stall: true }],
]) test(`${name} cannot satisfy identity`, async () => {
  const r = await fixture(options); assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /build identity verified/);
});
for (const manifest of ['{}', '{', JSON.stringify({ ...expected, commitSha: 'unverified' }),
  JSON.stringify({ ...expected, version: '' }), `${JSON.stringify(expected)}\n${JSON.stringify(expected)}`])
  test(`invalid selected metadata ${manifest.slice(0, 30)} refuses before HTTP`, async () => {
    const r = await fixture({ manifest }); assert.notEqual(r.code, 0); assert.deepEqual(r.requests, []);
  });
test('missing metadata argument refuses before HTTP', async () => {
  const r = await fixture({ noArgument: true }); assert.notEqual(r.code, 0); assert.deepEqual(r.requests, []);
});
test('invalid deadline refuses before HTTP', async () => {
  const r = await fixture({ timeout: '0' }); assert.notEqual(r.code, 0); assert.deepEqual(r.requests, []);
});
