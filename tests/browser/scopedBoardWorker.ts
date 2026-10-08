import { execFileSync } from 'node:child_process';
import { expect, type APIRequestContext } from '@playwright/test';

export async function waitForBoardDelivery(client: APIRequestContext, boardId: string) {
  let cursor = '0', observed = false;
  await expect.poll(async () => {
    const result = await client.get(`/boards/${boardId}/sync${cursor === '0' ? '' : `?since=${cursor}`}`);
    expect(result.status()).toBe(200);
    const body = await result.json();
    if (!body || typeof body.cursor !== 'string' || !/^[0-9]{1,19}$/.test(body.cursor)
      || BigInt(body.cursor) > 9223372036854775807n || typeof body.hasMore !== 'boolean'
      || typeof body.pending !== 'boolean' || typeof body.resetRequired !== 'boolean'
      || !Array.isArray(body.events) || body.events.length > 100)
      throw new Error('Invalid Board delivery page.');
    if (body.resetRequired) {
      if (body.cursor !== '0' || body.events.length || body.hasMore || body.pending)
        throw new Error('Invalid Board delivery reset.');
      cursor = '0'; observed = false; return false;
    }
    if (BigInt(body.cursor) - BigInt(cursor) !== BigInt(body.events.length)
      || body.hasMore && (body.pending || body.events.length !== 100))
      throw new Error('Invalid Board delivery progress.');
    observed ||= body.events.length > 0;
    cursor = body.cursor;
    // Pending is scoped to this bounded page. A ready first hundred does not
    // admit a later undelivered source. Follow actual cursors to the final page.
    return observed && !body.hasMore && !body.pending;
  }, { timeout: 30_000 }).toBe(true);
}

// Release scenarios run serially and reuse the immutable Worker image. Each
// scenario owns only its disposable Organization's delivery scope.
export function scopedBoardWorker(organizationId: string): () => void {
  // Demo journals simulate delivery in the API's original transaction. A
  // separate Production Worker cannot share that process-local provider.
  if (process.env.CI !== 'true' || process.env.STRATAAI_E2E_RUNTIME_MODE === 'demo') return () => {};
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
