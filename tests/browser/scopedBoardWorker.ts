import { execFileSync } from 'node:child_process';
import { expect, type APIRequestContext } from '@playwright/test';

export async function waitForBoardDelivery(client: APIRequestContext, boardId: string) {
  await expect.poll(async () => {
    const result = await client.get(`/boards/${boardId}/sync`);
    expect(result.status()).toBe(200);
    const body = await result.json();
    return body.pending === false && body.resetRequired === false && body.events.length > 0;
  }, { timeout: 30_000 }).toBe(true);
}

// Release scenarios run serially and reuse the immutable Worker image. Each
// scenario owns only its disposable Organization's delivery scope.
export function scopedBoardWorker(organizationId: string): () => void {
  if (process.env.CI !== 'true') return () => {};
  const files = ['-f', 'compose.release.yml', '-f', 'scripts/ci/compose.identity-test.yml'];
  const start = (scoped: boolean) => execFileSync('docker', ['compose', ...files,
    ...(scoped ? ['-f', 'scripts/ci/compose.work-event-test.yml'] : []),
    'up', '-d', '--no-deps', '--force-recreate', '--wait', '--wait-timeout', '180', 'worker'], {
    env: { ...process.env, STRATAAI_TEST_EVENT_ORGANIZATION_ID: organizationId }, stdio: 'pipe',
  });
  try { start(true); }
  catch (error) { start(false); throw error; }
  return () => { start(false); };
}
