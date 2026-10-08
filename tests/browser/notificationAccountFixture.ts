import { execFileSync } from 'node:child_process';
import type { APIRequestContext } from '@playwright/test';
import { expect } from './releaseTest';

// Provider delivery is exercised separately. Strict notification fixtures prove
// admission before/after verification without making private tokens public.
export async function registerNotificationAccount(request: APIRequestContext,
  credentials: { email: string; password: string; displayName: string }, requireVerifiedWorkerAccount = false) {
  const headers = { 'X-StrataAI-Request': '1' };
  const registered = await request.post('/auth/register', { headers, data: credentials });
  expect(registered.status()).toBe(201);
  const created = await registered.json();
  const strict = process.env.STRATAAI_E2E_VERIFY_NOTIFICATION_ACCOUNTS === '1';
  if (strict) {
    const pending = await request.post('/auth/login', { headers, data: credentials });
    expect(pending.status()).toBe(403);
    expect((await pending.json()).code).toBe('email_verification_required');
  }
  if (strict || requireVerifiedWorkerAccount) {
    if (created.verificationToken) {
      expect((await request.post('/auth/verify-email', { headers, data: { token: created.verificationToken } })).status()).toBe(200);
    } else {
      // Only the freshly registered disposable fixture account is activated.
      expect(process.env.CI).toBe('true');
      expect(created.user.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
      execFileSync('docker', ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c',
        'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'], {
        input: `UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,now()) WHERE id='${created.user.id}';`, stdio: 'pipe',
      });
    }
  }
  expect((await request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
  return created;
}
