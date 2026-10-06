import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03-WS-FR-009/TC-04/05/06/07/11/12: Owner confirms original deletion request at ${viewport.width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize(viewport);
    const admin = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    try {
      const users: string[] = []; const emails: string[] = [];
      for (const [index, client] of [context, admin].entries()) {
        const data = { email: `deletion-ui-${viewport.width}-${index}-${Date.now()}@example.test`,
          password: 'browser-deletion-ui-correct-horse', displayName: index ? 'Deletion administrator' : 'Deletion owner' };
        const registered = await client.request.post('/auth/register', { headers, data }); expect(registered.status()).toBe(201);
        users.push((await registered.json()).user.id); emails.push(data.email);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const created = await context.request.post('/organizations', { headers, data: { name: 'Owner deletion council' } }); expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id;
      const board = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Deletion access Board', visibility: 'ORGANIZATION' } });
      expect(board.status()).toBe(201); const boardId = (await board.json()).id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: emails[1], surface: 'INTERNAL', targetRole: 'ADMIN' } }); expect(invitation.status()).toBe(201);
      expect((await admin.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const before = await context.request.get(`/organizations/${org}`); expect(before.status()).toBe(200); const original = await before.json();
      const denied = await admin.request.delete(`/organizations/${org}?version=${original.organization.version}&expectedActorId=${users[1]}`,
        { headers: { ...headers, 'Idempotency-Key': '11111111-1111-4111-8111-111111111111' } }); expect(denied.status()).toBe(404);
      const adminPage = await admin.newPage(); await adminPage.goto(`/app/${org}/delete`);
      await expect(adminPage.getByText('Only a current Organization Owner can request deletion.')).toBeVisible();
      await expect(adminPage.getByRole('button', { name: 'Review deletion request', exact: true })).toHaveCount(0);
      await expect(adminPage.getByText('Owner deletion council', { exact: true })).toHaveCount(0);
      await page.goto(`/app/${org}`);
      await expect(page.getByRole('status').filter({ hasText: /^Current Board access checked\.$/ })).toBeVisible();
      const link = page.getByRole('link', { name: 'Request Organization deletion', exact: true });
      await link.focus(); await expect(link).toBeFocused(); await link.press('Enter');
      await expect(page).toHaveURL(new RegExp(`/app/${org}/delete$`));
      const launcher = page.getByRole('button', { name: 'Review deletion request', exact: true });
      await launcher.focus(); await launcher.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel deletion request', exact: true })).toBeFocused();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toHaveCount(0); await expect(launcher).toBeFocused();
      const unchanged = await context.request.get(`/organizations/${org}`); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(original);
      const writes: { url: string; key: string | undefined }[] = []; let privateReadsAfterIntent = 0;
      await page.route(url => url.pathname === `/organizations/${org}`, async route => {
        if (route.request().method() !== 'DELETE') { if (route.request().method() === 'GET') privateReadsAfterIntent++; await route.continue(); return; }
        writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'] });
        const url = new URL(route.request().url()); expect(url.searchParams.get('expectedActorId')).toBe(users[0]);
        expect(url.searchParams.get('version')).toBe(String(original.organization.version)); expect(writes.at(-1)!.key).toMatch(/^[0-9a-f-]{36}$/);
        const response = await route.fetch(); expect(response.status()).toBe(202);
        if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
      });
      await launcher.press('Enter'); const confirm = page.getByRole('button', { name: 'Confirm deletion request', exact: true });
      await confirm.focus(); await confirm.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original deletion request', exact: true }); await expect(retry).toBeFocused();
      await expect(page.getByRole('button', { name: 'Review current deletion permission', exact: true })).toBeDisabled();
      await expect(page.getByText('Owner deletion council', { exact: true })).toHaveCount(0); expect(writes).toHaveLength(1);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await admin.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await context.request.get(`/boards/${boardId}`)).status()).toBe(404);
      expect((await admin.request.get(`/boards/${boardId}`)).status()).toBe(404);
      // Refresh must recover the same reference without reopening ordinary
      // Organization admission or inventing another destructive request.
      await page.reload(); await expect(retry).toBeFocused();
      await expect(page.getByText('Owner deletion council', { exact: true })).toHaveCount(0);
      expect(writes).toHaveLength(1); expect(privateReadsAfterIntent).toBe(0);
      await retry.press('Enter'); const notice = page.getByRole('status');
      await expect(notice).toHaveText('Deletion request acknowledged. Deletion has not been confirmed complete.'); await expect(notice).toBeFocused();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      await expect(retry).toHaveCount(0); await expect(launcher).toHaveCount(0);
      const check = page.getByRole('button', { name: 'Check deletion status', exact: true });
      await check.focus(); await check.press('Enter');
      await expect(notice).toHaveText('Deletion is still in progress. Completion has not been confirmed.');
      await expect(check).toBeEnabled(); expect(privateReadsAfterIntent).toBe(0);
      expect((await admin.request.get(`/organizations/${org}/deletion-requests/${writes[0].key}`)).status()).toBe(404);
      await page.reload(); await expect(check).toBeEnabled(); expect(writes).toHaveLength(2); expect(privateReadsAfterIntent).toBe(0);
      await expect(page.getByText('Owner deletion council', { exact: true })).toHaveCount(0);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const directory = await context.request.get('/organizations/directory'); expect(directory.status()).toBe(200); expect((await directory.json()).items).toEqual([]);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
    } finally { await admin.close(); }
  });
}
