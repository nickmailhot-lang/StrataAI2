import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import test from 'node:test';

const bash = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';
const script = resolve('scripts/health-check.sh');
const endpoints = ['/api-surface/api/health', '/worker-surface/healthz',
  '/api-surface/readyz', '/worker-surface/readyz', '/web-surface/', '/web-surface/api/health'];
const privateBody = 'fixture-response-body-must-not-be-disclosed';

async function fixture(check, { failedPath, status = 200, stall = false, timeout = '2' } = {}) {
  const requests = [];
  const server = createServer((request, response) => {
    requests.push(request.url);
    if (stall) return;
    response.writeHead(request.url === failedPath ? status : 200,
      { 'Content-Type': 'text/plain', ...(status === 302 ? { Location: '/sign-in' } : {}) });
    response.end(privateBody);
  });
  await new Promise((done, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', done); });
  const origin = `http://127.0.0.1:${server.address().port}`;
  let watchdog;
  try {
    const started = performance.now();
    const child = spawn(bash, [script], { env: { ...process.env,
      STRATAAI_API_URL: `${origin}/api-surface`, STRATAAI_WORKER_URL: `${origin}/worker-surface`,
      STRATAAI_WEB_URL: `${origin}/web-surface`, STRATAAI_HEALTH_TIMEOUT_SECONDS: timeout } });
    let deadlineExceeded = false;
    watchdog = setTimeout(() => {
      deadlineExceeded = true; server.closeAllConnections(); child.kill('SIGKILL');
    }, 8_000);
    let output = '';
    child.stdout.on('data', chunk => { output += chunk; });
    child.stderr.on('data', chunk => { output += chunk; });
    const code = await new Promise((done, reject) => { child.once('error', reject); child.once('close', done); });
    clearTimeout(watchdog);
    assert.equal(deadlineExceeded, false, 'Health command exceeded the test deadline');
    assert.equal(output.includes(privateBody), false);
    await check({ code, output, requests, duration: performance.now() - started });
  } finally {
    clearTimeout(watchdog);
    server.closeAllConnections();
    await new Promise(done => server.close(done));
  }
}

test('all six HTTP 200 health/readiness endpoints admit promotion without disclosing bodies', { timeout: 15_000 }, async () => {
  await fixture(({ code, output, requests }) => {
    assert.equal(code, 0); assert.deepEqual(requests, endpoints);
    assert.match(output, /health checks passed/);
  });
});

for (const failedPath of endpoints) {
  for (const status of [302, 503]) {
    test(`${status === 302 ? 'redirect' : 'unavailable'} at ${failedPath} rejects promotion`, { timeout: 15_000 }, async () => {
      await fixture(({ code, output, requests }) => {
        assert.notEqual(code, 0); assert.equal(/health checks passed/.test(output), false);
        assert.deepEqual(requests, endpoints.slice(0, endpoints.indexOf(failedPath) + 1));
      }, { failedPath, status });
    });
  }
}

test('a stalled health response fails within the configured request budget', { timeout: 10_000 }, async () => {
  await fixture(({ code, output, requests, duration }) => {
    assert.notEqual(code, 0); assert.equal(/health checks passed/.test(output), false);
    assert.deepEqual(requests, [endpoints[0]]); assert.ok(duration < 5_000);
  }, { stall: true, timeout: '1' });
});

for (const timeout of ['0', '301', '1e1', 'invalid']) {
  test(`invalid health timeout ${timeout} rejects before any request`, { timeout: 15_000 }, async () => {
    await fixture(({ code, output, requests }) => {
      assert.notEqual(code, 0); assert.deepEqual(requests, []);
      assert.match(output, /Health timeout must be an integer from 1 to 300 seconds/);
    }, { timeout });
  });
}
