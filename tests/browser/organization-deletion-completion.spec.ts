import { execFileSync } from 'node:child_process';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

// Uses the exact release Worker already loaded by CI. No privileged SQL creates
// terminal state, advances progress, marks jobs ready or fabricates snapshots.
function deletionWorker(enabled: boolean) {
  const env: NodeJS.ProcessEnv = { ...process.env, STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED: String(enabled) };
  if (enabled) env.STRATAAI_WORKER_ORGANIZATION_IDS = '';
  execFileSync('docker', ['compose', '-f', 'compose.release.yml', '-f', 'scripts/ci/compose.auth-test.yml',
    '-f', 'scripts/ci/compose.identity-test.yml', 'up', '-d', '--no-deps', '--force-recreate', '--wait', '--wait-timeout', '45', 'worker'], {
    env,
    stdio: 'pipe', timeout: 60_000,
  });
}

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03-WS-FR-010/TC-01/06/07/10/11/12: Worker finishes before lost acknowledgment recovery at ${viewport.width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000);
    expect(process.env.CI).toBe('true');
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `terminal-deletion-${viewport.width}-${Date.now()}@example.test`,
      password: 'browser-terminal-deletion-correct-horse', displayName: 'Deletion completion Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: credentials });
    expect(registered.status()).toBe(201); const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Terminal deletion council' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization;
    const org = organization.id; const version = organization.version;
    const member = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    try {
    const memberCredentials = { email: `terminal-member-${viewport.width}-${Date.now()}@example.test`,
      password: 'browser-terminal-member-correct-horse', displayName: 'Deletion observer Member' };
    const memberRegistration = await member.request.post('/auth/register', { headers, data: memberCredentials });
    expect(memberRegistration.status()).toBe(201); const memberActor = (await memberRegistration.json()).user.id;
    expect((await member.request.post('/auth/login', { headers, data: memberCredentials })).status()).toBe(200);
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
      data: { email: memberCredentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const lifecyclePath = `/organizations/${org}/lifecycle-events?expectedActorId=${memberActor}`;
    const initialLifecycle = await member.request.get(lifecyclePath); expect(initialLifecycle.status()).toBe(200);
    expect(await initialLifecycle.json()).toEqual({ state: 'ACTIVE', events: [] });
    const board = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Terminal deletion Board' } });
    expect(board.status()).toBe(201); const boardId = (await board.json()).id;
    const list = await context.request.post(`/boards/${boardId}/lists`, { headers, data: { name: 'Terminal deletion List' } });
    expect(list.status()).toBe(201); const listId = (await list.json()).id;
    const card = await context.request.post(`/lists/${listId}/cards`, { headers, data: { title: 'Terminal deletion Card' } });
    expect(card.status()).toBe(201); const cardId = (await card.json()).id;
    const writes: string[] = []; let ordinaryReads = 0;
    await page.goto(`/app/${org}/delete`);
    const launcher = page.getByRole('button', { name: 'Review deletion request', exact: true });
    await expect(launcher).toBeEnabled();
    await page.route(url => url.pathname === `/organizations/${org}`, async route => {
      if (route.request().method() !== 'DELETE') {
        if (route.request().method() === 'GET') ordinaryReads++;
        await route.continue(); return;
      }
      const key = route.request().headers()['idempotency-key']; expect(key).toMatch(/^[0-9a-f-]{36}$/); writes.push(key);
      const response = await route.fetch(); expect(response.status()).toBe(202);
      expect(await response.json()).toEqual({ requestId: key });
      if (writes.length === 1) await route.abort('failed'); else await route.fulfill({ response });
    });
    await launcher.focus(); await launcher.press('Enter');
    const cancel = page.getByRole('button', { name: 'Cancel deletion request', exact: true }); await expect(cancel).toBeFocused();
    const confirm = page.getByRole('button', { name: 'Confirm deletion request', exact: true }); await confirm.focus(); await confirm.press('Enter');
    const retry = page.getByRole('button', { name: 'Retry original deletion request', exact: true });
    await expect(retry).toBeFocused(); expect(writes).toHaveLength(1);
    const pendingLifecycle = await member.request.get(lifecyclePath); expect(pendingLifecycle.status()).toBe(200);
    expect(await pendingLifecycle.json()).toEqual({ state: 'PENDING', events: [] });
    const key = writes[0]; const statusPath = `/organizations/${org}/deletion-requests/${key}?expectedActorId=${actor}`;
    let workerStarted = false;
    try {
      workerStarted = true; deletionWorker(true);
      await expect.poll(async () => {
        const observed = await context.request.get(statusPath); expect(observed.status()).toBe(200);
        return (await observed.json()).state;
      }, { timeout: 30_000 }).toBe('COMPLETED');
      const completed = await context.request.get(statusPath); const snapshot = await completed.json();
      expect(snapshot).toEqual({ requestId: key, state: 'COMPLETED', version: version + 2,
        eventId: expect.stringMatching(/^[0-9a-f-]{36}$/), completedAt: expect.any(String) });
      expect(completed.headers()['cache-control']).toContain('no-store');
      await expect.poll(async () => {
        const response = await member.request.get(lifecyclePath); expect(response.status()).toBe(200);
        return (await response.json()).state;
      }, { timeout: 30_000 }).toBe('COMPLETED');
      const terminalLifecycle = await member.request.get(lifecyclePath);
      expect(terminalLifecycle.headers()['cache-control']).toContain('no-store');
      expect(await terminalLifecycle.json()).toEqual({ state: 'COMPLETED', events: [{
        eventId: snapshot.eventId, eventType: 'ORGANIZATION_DELETED', actorId: actor,
        organizationId: org, boardId: null, entityType: 'Organization', entityId: org,
        version: snapshot.version, metadata: {}, createdAt: snapshot.completedAt,
      }] });
      expect((await member.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await member.request.get(`/organizations/${org}/deletion-requests/${key}`)).status()).toBe(404);
      expect((await member.request.get(`/organizations/${org}/lifecycle-events?expectedActorId=${actor}`)).status()).toBe(401);
      // This reload precedes acknowledgment replay. Terminal parent admission
      // must recover the original uncertain intent without normal graph reads.
      await page.reload(); await expect(retry).toBeFocused(); expect(writes).toHaveLength(1); expect(ordinaryReads).toBe(0);
      await expect(page.getByText('Terminal deletion council', { exact: true })).toHaveCount(0);
      await retry.press('Enter'); const notice = page.getByRole('status');
      await expect(notice).toHaveText('Deletion request acknowledged. Deletion has not been confirmed complete.');
      await expect(notice).toBeFocused(); expect(writes).toEqual([key, key]);
      const check = page.getByRole('button', { name: 'Check deletion status', exact: true });
      await check.focus(); await check.press('Enter');
      await expect(notice).toHaveText('Organization deletion confirmed complete.'); await expect(notice).toBeFocused();
      await expect(check).toHaveCount(0);
      const repeated = await context.request.get(statusPath); expect(repeated.status()).toBe(200); expect(await repeated.json()).toEqual(snapshot);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await context.request.get(`/boards/${boardId}`)).status()).toBe(404);
      expect((await context.request.get(`/cards/${cardId}`)).status()).toBe(404);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      // Completed snapshots are not cached: another refresh restores only the
      // original accepted reference, then checks current authority again.
      await page.reload(); await expect(check).toBeEnabled();
      await expect(page.getByText('Organization deletion confirmed complete.', { exact: true })).toHaveCount(0);
      await check.focus(); await check.press('Enter');
      await expect(notice).toHaveText('Organization deletion confirmed complete.'); await expect(notice).toBeFocused();
      expect(writes).toEqual([key, key]); expect(ordinaryReads).toBe(0);
      expect((await member.request.post('/auth/logout', { headers, data: {} })).status()).toBe(204);
      const withdrawn = await member.request.get(lifecyclePath); expect(withdrawn.status()).toBe(401);
      expect(await withdrawn.text()).not.toContain(snapshot.eventId);
    } finally { if (workerStarted) deletionWorker(false); }
    } finally { await member.close(); }
  });
}
