import { execFileSync } from 'node:child_process';

// Local Production runs supply a separate automatic Worker. Release scenarios
// use the exact retained image and restore the job's original discovery flags.
export function automaticDeletionWorker(): () => void {
  // Demo's accepted-request provider runs in its API-owned in-memory host.
  // Never start a production Worker or database topology for this fixture.
  if (process.env.STRATAAI_E2E_RUNTIME_MODE === 'demo') return () => {};
  if (process.env.CI !== 'true') return () => {};
  const start = (enabled: boolean) => execFileSync('docker', ['compose', '-f', 'compose.release.yml',
    '-f', 'scripts/ci/compose.auth-test.yml', '-f', 'scripts/ci/compose.identity-test.yml',
    'up', '-d', '--no-deps', '--force-recreate', '--wait', '--wait-timeout', '45', 'worker'], {
    env: { ...process.env, ...(enabled ? { STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED: 'true', STRATAAI_WORKER_ORGANIZATION_IDS: '' } : {}) },
    stdio: 'pipe', timeout: 60_000,
  });
  try { start(true); }
  catch (error) { start(false); throw error; }
  return () => { start(false); };
}
