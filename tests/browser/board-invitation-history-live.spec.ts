import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-05/60-TC-06/09/11/12: Board invitation history reconciles issuance, acceptance and revoked consent at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; let restoreWorker = () => {};
    try {
      const accounts: { id: string; email: string }[] = [];
      for (const [index, client] of [context, recipient].entries()) {
        const data = { email: `board-history-live-${width}-${index}-${Date.now()}@example.test`,
          password: 'board-history-live-correct-horse', displayName: 'Board history live account' };
        const result = await client.request.post('/auth/register', { headers, data }); expect(result.status()).toBe(201);
        accounts.push((await result.json()).user);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers, data: { name: 'Live Board history Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const result = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Live invitation Board' } });
      expect(result.status()).toBe(201); const board = (await result.json()).id;
      restoreWorker = scopedBoardWorker(org);
      let documents = 0, observerWrites = 0;
      page.on('request', request => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
        if (['POST', 'DELETE'].includes(request.method()) && new URL(request.url()).pathname.startsWith(`/boards/${board}/invitations`)) observerWrites++;
      });
      await page.goto(`/app/${org}/boards/${board}/invitations`);
      await expect(page.getByText('Current invitations checked. Review an invitation again before confirming revocation.', { exact: true })).toBeVisible();
      await expect(page.getByText('No issued invitations on this page.', { exact: true })).toBeVisible();
      const issue = async (role: string) => {
        const response = await context.request.post(`/boards/${board}/invitations`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
          data: { email: accounts[1].email, role } });
        expect(response.status()).toBe(201); return (await response.json()).id as string;
      };
      const acceptedId = await issue('MEMBER');
      const pendingAction = page.getByRole('button', { name: `Revoke invitation for ${accounts[1].email}`, exact: true });
      await expect(pendingAction).toBeEnabled({ timeout: 30_000 });
      await pendingAction.focus(); await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toBeVisible();
      expect((await recipient.request.post(`/me/invitations/${acceptedId}/accept`, { headers })).status()).toBe(200);
      await expect(page.getByRole('dialog')).toHaveCount(0, { timeout: 30_000 });
      await expect(page.getByRole('article').filter({ hasText: 'Accepted' })).toHaveCount(1);
      await expect(pendingAction).toHaveCount(0);
      const revokedId = await issue('ADMIN');
      await expect(pendingAction).toBeEnabled({ timeout: 30_000 });
      await pendingAction.focus(); await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toBeVisible();
      // Disconnect the observing client while another client revokes the
      // pending upgrade. Reconnect must retire old consent and read real history.
      await context.setOffline(true);
      expect((await context.request.delete(`/boards/${board}/invitations/${revokedId}`, { headers })).status()).toBe(204);
      await context.setOffline(false);
      await expect(page.getByRole('dialog')).toHaveCount(0, { timeout: 30_000 });
      await expect(page.getByRole('article').filter({ hasText: 'Revoked' })).toHaveCount(1);
      await expect(page.getByRole('article').filter({ hasText: 'Accepted' })).toHaveCount(1);
      await expect(pendingAction).toHaveCount(0);
      const history = await context.request.get(`/boards/${board}/invitations`); expect(history.status()).toBe(200);
      const rows = (await history.json()).items; expect(rows).toHaveLength(2);
      expect(rows.find((row: { id: string }) => row.id === acceptedId)).toMatchObject({ acceptedAt: expect.any(String), revokedAt: null });
      expect(rows.find((row: { id: string }) => row.id === revokedId)).toMatchObject({ acceptedAt: null, revokedAt: expect.any(String) });
      const readable = await recipient.request.get(`/boards/${board}`); expect(readable.status()).toBe(200);
      expect((await readable.json()).access.canAdminister).toBe(false);
      const denied = await recipient.request.post(`/me/invitations/${revokedId}/accept`, { headers }); expect(denied.status()).toBe(400);
      expect(documents).toBe(1); expect(observerWrites).toBe(0);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await context.setOffline(false); restoreWorker(); await recipient.close(); }
  });
}
