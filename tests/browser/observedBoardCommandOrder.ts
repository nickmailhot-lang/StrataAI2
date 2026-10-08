import { execFileSync, spawn } from 'node:child_process';
import type { APIResponse } from '@playwright/test';
import { expect } from './releaseTest';

const sqlArgs = ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c',
  'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'];
export const commandOrderQuery = (sql: string) => execFileSync('docker', sqlArgs,
  { input: sql, encoding: 'utf8', stdio: 'pipe' }).trim();

// Observe actual blocked requests and their peer blocker before releasing the
// real Board gate. Only focus-free HTTP commands are admitted during this wait.
export async function inObservedBoardOrder(org: string, board: string,
  first: () => Promise<APIResponse>, second: () => Promise<APIResponse>) {
  expect(process.env.CI).toBe('true');
  for (const id of [org, board]) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  const waiters = () => JSON.parse(commandOrderQuery(`SELECT COALESCE(jsonb_agg(pid ORDER BY pid),'[]') FROM pg_stat_activity WHERE datname=current_database() AND usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';`)) as number[];
  const gate = spawn('docker', sqlArgs, { stdio: 'pipe' }); let output = '', closed = false, exitCode: number | null = null;
  gate.stdout.on('data', chunk => { output += chunk.toString(); }); gate.stderr.on('data', () => {});
  gate.on('close', code => { closed = true; exitCode = code; }); gate.on('error', () => { closed = true; });
  const pending: Promise<APIResponse>[] = [];
  try {
    gate.stdin.write(`BEGIN; SELECT id FROM boards WHERE tenant_id='${org}' AND id='${board}' FOR UPDATE;\n\\echo trigger_gate_locked\n`);
    await expect.poll(() => output.includes('trigger_gate_locked')).toBe(true);
    pending.push(first()); await expect.poll(() => waiters().length).toBe(1); const firstPid = waiters()[0];
    pending.push(second()); await expect.poll(() => waiters().length).toBe(2); const secondPid = waiters().find(pid => pid !== firstPid)!;
    expect(Number.isInteger(firstPid) && firstPid > 0 && Number.isInteger(secondPid) && secondPid > 0).toBe(true);
    await expect.poll(() => commandOrderQuery(`SELECT ${firstPid}=ANY(pg_blocking_pids(${secondPid}));`)).toBe('t');
    gate.stdin.end('COMMIT;\n\\q\n'); await expect.poll(() => closed).toBe(true); expect(exitCode).toBe(0);
    return await Promise.all(pending);
  } finally {
    if (!closed) { gate.stdin.end('ROLLBACK;\n\\q\n'); await expect.poll(() => closed, { timeout: 10_000 }).toBe(true); }
    await Promise.allSettled(pending);
  }
}
