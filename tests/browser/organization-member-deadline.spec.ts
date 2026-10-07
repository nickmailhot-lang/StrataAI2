import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-05/06/07/11/12: complete member removal deadline preserves its real committed original at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    let releaseProfile!: () => void, releaseRemoval!: () => void;
    const profileGate = new Promise<void>(resolve => { releaseProfile = resolve; });
    const removalGate = new Promise<void>(resolve => { releaseRemoval = resolve; });
    try {
      const users: string[] = [];
      const accounts = [context, recipient];
      for (const [index, client] of accounts.entries()) {
        const account = { email: `member-deadline-${width}-${index}-${Date.now()}@example.test`, password: 'member-deadline-correct-horse', displayName: index ? 'Deadline participant' : 'Deadline Owner' };
        const registered = await client.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201); users.push((await registered.json()).user.id);
        expect((await client.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      }
      const created = await context.request.post('/organizations', { headers, data: { name: 'Member deadline council' } }); expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id;
      const email = (await (await recipient.request.get('/me')).json()).email;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } }); expect(invitation.status()).toBe(201);
      expect((await recipient.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      let documents = 0; page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      await page.goto(`/app/${org}/members`);
      if ((await (await context.request.get('/api/runtime')).json()).mode === 'production')
        await expect(page.getByText('Current members checked. Review a membership again before confirming removal.', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Review removal of Deadline participant', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel removal', exact: true })).toBeFocused();
      let profileHeld = false, removalHeld = false;
      const writes: { url: string; key: string; body: string | null }[] = [];
      await page.route(url => url.pathname === '/me', async route => {
        if (profileHeld) { await route.continue(); return; }
        const response = await route.fetch(); expect(response.status()).toBe(200); profileHeld = true; await profileGate;
        await route.fulfill({ response }).catch(() => {});
      });
      await page.route(url => url.pathname === `/organizations/${org}/members/${users[1]}`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const response = await route.fetch(); expect(response.status()).toBe(204);
        if (writes.length === 1) { removalHeld = true; await removalGate; }
        await route.fulfill({ response }).catch(() => {});
      });
      await page.clock.install(); await page.getByRole('button', { name: 'Confirm member removal', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect.poll(() => profileHeld).toBe(true); await page.clock.runFor(8000); releaseProfile();
      await expect.poll(() => removalHeld).toBe(true); await page.clock.runFor(7001); await page.clock.runFor(500);
      const retry = page.getByRole('button', { name: 'Retry original removal', exact: true }); await expect(retry).toBeEnabled();
      await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByText(email, { exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Load current members', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Review current membership', exact: true })).toBeDisabled();
      expect(writes).toHaveLength(1); await expect(page.getByText('Member removed.', { exact: true })).toHaveCount(0);
      releaseRemoval(); await page.clock.runFor(500); await expect(page.getByText('Member removed.', { exact: true })).toHaveCount(0);
      await retry.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Original removal acknowledged. Review current membership to check later access.', { exact: true })).toBeVisible();
      await expect(page.getByRole('status')).toBeFocused(); expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(documents).toBe(1);
      const exact = await context.request.get(`/organizations/${org}/members/${users[1]}`); expect(exact.status()).toBe(200); expect((await exact.json()).member).toBeNull();
      expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(404);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { releaseProfile(); releaseRemoval(); await recipient.close(); }
  });
}
