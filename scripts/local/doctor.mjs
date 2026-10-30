import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { arch, platform } from 'node:os';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
export function doctor() {
  const pkg = JSON.parse(readFileSync(new URL('../../package.json', import.meta.url)));
  const sdk = JSON.parse(readFileSync(new URL('../../global.json', import.meta.url))).sdk;
  const checks = [
    ['node', ['--version'], v => Number(v.replace(/^v/, '').split('.')[0]) === 24],
    ['npm', ['--version'], v => v === pkg.packageManager.split('@')[1]],
    ['git', ['--version'], v => /^git version /.test(v)],
    ['dotnet', ['--version'], v => v.split('.')[0] === sdk.version.split('.')[0]],
    ['docker', ['version', '--format', '{{.Server.Version}}'], v => !!v],
    ['docker', ['context', 'inspect', '--format', '{{.Endpoints.docker.Host}}'], v => /^(unix|npipe):\/\//.test(v) && (!process.env.DOCKER_HOST || /^(unix|npipe):\/\//.test(process.env.DOCKER_HOST))],
    ['docker', ['compose', 'version', '--short'], v => /^v?[2-9]\d*\./.test(v)],
    ['python3', ['--version'], v => /^Python 3\./.test(v) && Number(v.split('.')[1]) >= 11],
    ['jq', ['--version'], v => /^jq-/.test(v)],
    ['bash', ['--version'], v => /version [5-9]\./.test(v)],
    ['openssl', ['version'], v => /^OpenSSL [3-9]\./.test(v)],
    ['ln', ['--version'], v => /GNU coreutils/.test(v)],
    ['sha256sum', ['--version'], v => /GNU coreutils/.test(v)],
    ['timeout', ['--version'], v => /GNU coreutils/.test(v)],
    ['wc', ['--version'], v => /GNU coreutils/.test(v)],
  ];
  const results = checks.map(([tool, args, validate]) => {
    const r = spawnSync(tool, args, { encoding: 'utf8', timeout: 20_000 });
    const version = (r.stdout ?? '').trim();
    return { tool, args, passed: r.status === 0 && validate(version), version: version.split('\n')[0], error: r.error?.message };
  });
  return { platform: platform(), architecture: arch(), sdkPolicy: sdk, checks: results, passed: results.every(r => r.passed) };
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = doctor(); console.log(JSON.stringify(result, null, 2)); process.exitCode = result.passed ? 0 : 1;
}
