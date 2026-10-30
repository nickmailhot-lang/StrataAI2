import { spawnSync } from 'node:child_process';
import { rmSync } from 'node:fs';
import { context, snapshot, materialize } from './repository.mjs';
const root = materialize(snapshot(context().root));
try {
  const result = spawnSync('docker', ['run', '--rm', '-v', `${root}:/workspace:ro`, 'aquasec/trivy:0.74.0', 'fs', '--scanners', 'secret', '--exit-code', '1', '/workspace'], { stdio: 'inherit', timeout: 10 * 60 * 1000 });
  if (result.error || result.status !== 0) throw new Error('Candidate secret scan failed.');
} finally { rmSync(root, { recursive: true, force: true }); }
