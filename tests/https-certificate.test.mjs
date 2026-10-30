import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createServer as httpsServer } from 'node:https';
import { createServer as tcpServer } from 'node:net';
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { after, before, test } from 'node:test';

import { testBash as bash } from './local-test-tools.mjs';
const script = resolve('scripts/verify-https-certificate.sh');
let root;
before(() => {
  root = mkdtempSync(join(tmpdir(), 'strataai-certificate-tests-'));
  const run = command => {
    const r = spawnSync(bash, ['-c', command], { cwd: root, encoding: 'utf8', timeout: 20000 });
    assert.equal(r.status, 0, 'Local certificate fixture generation must succeed');
  };
  run('MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 3 -subj /CN=localhost -addext subjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1 -keyout good.key -out good.crt');
  run('MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 3 -subj /CN=PRIVATE_CERTIFICATE_SUBJECT -addext subjectAltName=DNS:wrong.invalid -keyout wrong.key -out wrong.crt');
  run('MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 -subj /CN=PRIVATE_CERTIFICATE_SUBJECT -keyout issuer.key -out issuer.crt');
  writeFileSync(join(root, 'extensions.cnf'), '[leaf]\nsubjectAltName=DNS:localhost,IP:127.0.0.1\nbasicConstraints=critical,CA:FALSE\nextendedKeyUsage=serverAuth\n');
  run('MSYS_NO_PATHCONV=1 openssl req -new -key good.key -subj /CN=localhost -out leaf.csr');
  run('openssl x509 -req -in leaf.csr -CA issuer.crt -CAkey issuer.key -CAcreateserial -days 3 -extfile extensions.cnf -extensions leaf -out issuer-leaf.crt');
  writeFileSync(join(root, 'index'), ''); writeFileSync(join(root, 'serial'), '10\n');
  writeFileSync(join(root, 'ca.cnf'), '[ca]\ndefault_ca=fixture\n[fixture]\ndatabase=index\nserial=serial\nnew_certs_dir=.\ndefault_md=sha256\npolicy=fixture_policy\n[fixture_policy]\ncommonName=supplied\n');
  run('openssl ca -batch -notext -config ca.cnf -cert issuer.crt -keyfile issuer.key -startdate 20200101000000Z -enddate 20200102000000Z -extfile extensions.cnf -extensions leaf -in leaf.csr -out expired.crt');
});
after(() => {
  if (!root) return;
  assert.equal(dirname(realpathSync(root)), realpathSync(tmpdir())); assert.match(basename(root), /^strataai-certificate-tests-/);
  rmSync(root, { recursive: true, force: true });
});

async function fixture(options = {}) {
  const folder = mkdtempSync(join(root, 'case-')); const scratch = join(folder, 'scratch'); mkdirSync(scratch);
  const sockets = new Set(), serverNames = []; let connections = 0, timer, child;
  const name = options.certificate ?? 'good';
  const cert = readFileSync(join(root, `${name}.crt`));
  const chain = name === 'issuer-leaf' || name === 'expired' ? Buffer.concat([cert, readFileSync(join(root, 'issuer.crt'))])
    : options.large ? Buffer.concat(Array(80).fill(cert)) : cert;
  const server = options.stall ? tcpServer() : httpsServer({ key: readFileSync(join(root, name === 'issuer-leaf' || name === 'expired' ? 'good.key' : `${name}.key`)), cert: chain });
  server.on('tlsClientError', () => {});
  server.on('secureConnection', socket => serverNames.push(socket.servername));
  server.on('connection', socket => { connections++; sockets.add(socket); socket.on('error', () => {}); socket.on('close', () => sockets.delete(socket)); });
  try {
    await new Promise(done => server.listen(0, options.ipv6 ? '::1' : options.host === 'localhost' ? '::' : '127.0.0.1', done));
    const args = options.args ?? [options.host ?? (options.ipv6 ? '::1' : '127.0.0.1'), String(server.address().port), options.margin ?? '3600',
      join(root, options.untrusted ? 'wrong.crt' : name === 'issuer-leaf' || name === 'expired' ? 'issuer.crt' : `${name}.crt`)];
    child = spawn(bash, [script, ...args], { env: { ...process.env, BASH_ENV: undefined, TMPDIR: scratch, STRATAAI_TLS_TIMEOUT_SECONDS: options.timeout ?? '3' } });
    let output = '', overdue = false;
    child.stdout.on('data', data => { output += data; }); child.stderr.on('data', data => { output += data; });
    timer = setTimeout(() => { overdue = true; for (const s of sockets) s.destroy(); child.kill('SIGKILL'); }, 8000);
    const code = await new Promise((done, reject) => { child.once('error', reject); child.once('close', done); });
    clearTimeout(timer); assert.equal(overdue, false, 'Internal deadline must precede the test watchdog');
    assert.doesNotMatch(output, /BEGIN CERTIFICATE|PRIVATE_CERTIFICATE_SUBJECT|PRIVATE KEY|verify error|issuer=/);
    assert.deepEqual(readdirSync(scratch), [], 'Private peer/certificate scratch files must be removed');
    return { code, output, connections, serverNames };
  } finally {
    clearTimeout(timer); for (const s of sockets) s.destroy(); await new Promise(done => server.close(done));
    assert.equal(dirname(realpathSync(folder)), realpathSync(root)); assert.match(basename(folder), /^case-/);
    rmSync(folder, { recursive: true, force: true });
  }
}

test('valid trusted IPv4 certificate and configured lifetime pass', async () => {
  const r = await fixture(); assert.equal(r.code, 0, r.output); assert.match(r.output, /remaining lifetime verified/);
});
test('valid trusted IPv6 certificate passes IP identity verification', async () => {
  const r = await fixture({ ipv6: true }); assert.equal(r.code, 0, r.output);
});
test('DNS identity uses SNI and hostname verification', async () => {
  const r = await fixture({ host: 'localhost' }); assert.equal(r.code, 0, r.output);
  assert.deepEqual(r.serverNames, ['localhost']);
});
test('currently valid issuer and long-lived leaf pass a small horizon', async () => {
  const r = await fixture({ certificate: 'issuer-leaf' }); assert.equal(r.code, 0, r.output);
});
for (const [name, options] of [
  ['wrong identity', { certificate: 'wrong' }], ['wrong DNS identity', { certificate: 'wrong', host: 'localhost' }], ['untrusted peer', { untrusted: true }],
  ['expired peer', { certificate: 'expired' }], ['leaf expires before horizon', { margin: '345600' }],
  ['issuer expires before long-lived leaf', { certificate: 'issuer-leaf', margin: '172800' }],
  ['oversized peer output', { large: true }], ['stalled handshake', { stall: true, timeout: '1' }],
]) test(`${name} refuses verification`, async () => {
  const r = await fixture(options); assert.notEqual(r.code, 0); assert.doesNotMatch(r.output, /remaining lifetime verified/);
});
for (const args of [[], ['user@host', '443', '3600'], ['host/path', '443', '3600'], ['host', '0', '3600'],
  ['host', '65536', '3600'], ['host', '443', '0'], ['host', '443', '31536001'], ['host', '443', '1e3'], ['host', '443', '3600', 'missing-ca.pem']])
  test(`invalid explicit inputs ${JSON.stringify(args)} refuse before connecting`, async () => {
    const r = await fixture({ args }); assert.notEqual(r.code, 0); assert.equal(r.connections, 0);
  });
for (const timeout of ['', '0', '61', '1.5']) test(`invalid deadline ${JSON.stringify(timeout)} refuses before connecting`, async () => {
  const r = await fixture({ timeout }); assert.notEqual(r.code, 0); assert.equal(r.connections, 0);
});
