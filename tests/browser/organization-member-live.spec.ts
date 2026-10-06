import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/08/09/11/12: live member addition retires consent and preserves removal recovery at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(240_000); await page.setViewportSize({ width, height: 844 });
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const newcomer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; let restoreWorker = () => {};
    try {
      const accounts: { id: string; email: string }[] = [];
      for (const [index, client] of [context, recipient, newcomer].entries()) {
        const data = { email: `member-live-${width}-${index}-${Date.now()}@example.test`, password: 'live-members-correct-horse',
          displayName: ['Live member Owner', 'Live joined member', 'Later joined member'][index] };
        const registration = await client.request.post('/auth/register', { headers, data }); expect(registration.status()).toBe(201);
        accounts.push((await registration.json()).user);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers, data: { name: 'Live member Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      restoreWorker = scopedBoardWorker(org);
      const delivered: Array<Record<string, unknown>> = [];
      page.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type === 2 && message.item?.organizationId === org)
              delivered.push(...message.item.page.events.filter((row: Record<string, unknown>) => row.eventType === 'ORGANIZATION_MEMBER_ADDED'));
          }
        });
      });
      await page.goto(`/app/${org}/members`);
      await expect(page.getByText('Current members checked. Review a membership again before confirming removal.', { exact: true })).toBeVisible();
      async function join(index: number, client: typeof recipient) {
        const issued = await context.request.post(`/organizations/${org}/invitations`, { headers,
          data: { email: accounts[index].email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
        expect(issued.status()).toBe(201);
        expect((await client.request.post(`/me/invitations/${(await issued.json()).id}/accept`, { headers })).status()).toBe(200);
      }
      await join(1, recipient);
      await expect.poll(() => delivered.filter(row => row.actorId === accounts[1].id && row.version === 1).length, { timeout: 30_000 }).toBe(1);
      const action = page.getByRole('button', { name: 'Review removal of Live joined member', exact: true });
      await expect(action).toBeVisible(); await action.focus(); await action.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel removal' })).toBeFocused();
      await join(2, newcomer);
      await expect.poll(() => delivered.filter(row => row.actorId === accounts[2].id).length, { timeout: 30_000 }).toBe(1);
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Later joined member', exact: true })).toBeVisible();
      await action.focus(); await action.press('Enter'); await expect(page.getByRole('button', { name: 'Cancel removal' })).toBeFocused();
      const writes: { url: string; key: string | undefined }[] = [];
      await page.route(`**/organizations/${org}/members/${accounts[1].id}?*`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'] });
        const response = await route.fetch(); expect(response.status()).toBe(204);
        if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
      });
      await page.getByRole('button', { name: 'Confirm member removal' }).press('Enter');
      await expect(page.getByRole('button', { name: 'Retry original removal' })).toBeEnabled();
      await join(1, recipient);
      await expect.poll(() => delivered.filter(row => row.actorId === accounts[1].id && row.version === 3).length, { timeout: 30_000 }).toBe(1);
      await expect(page.getByText('Current access checked. Retry the original removal before reviewing later membership.', { exact: true })).toBeVisible();
      await expect(page.getByText(accounts[1].email, { exact: true })).toHaveCount(0); expect(writes).toHaveLength(1);
      const current = await context.request.get(`/organizations/${org}/members/${accounts[1].id}`); expect(current.status()).toBe(200);
      const restored = (await current.json()).member; expect(restored.version).toBe(3);
      const source = delivered.find(row => row.actorId === accounts[1].id && row.version === 3)!;
      expect(source.entityType).toBe('OrganizationMembership'); expect(source.entityId).toBe(restored.membershipId);
      expect(source.boardId).toBeNull(); expect(source.metadata).toEqual({});
      const retry = page.getByRole('button', { name: 'Retry original removal' }); await retry.focus(); await retry.press('Enter');
      await expect(page.getByText('Original removal acknowledged. Review current membership to check later access.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect((await (await context.request.get(`/organizations/${org}/members/${accounts[1].id}`)).json()).member).toEqual(restored);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { await recipient.close(); await newcomer.close(); restoreWorker(); }
  });
}
