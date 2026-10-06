import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-01/03/11/12: confirmed departure preserves sole-owner continuity at ${width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `departure-owner-${width}-${Date.now()}@example.test`, password: 'departure-correct-horse-battery', displayName: 'Departure owner' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Departure Organization' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    await page.goto(`/app/${org}`);
    await page.getByRole('link', { name: 'Leave Organization', exact: true }).focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Review departure' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Cancel departure' })).toBeFocused();
    await page.getByRole('button', { name: 'Confirm departure' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('The last usable owner cannot leave. Another usable owner must remain.')).toBeVisible();
    expect((await context.request.get(`/organizations/${org}`)).status()).toBe(200);
    const member = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    try {
      const credentials = { ...account, email: `departure-member-${width}-${Date.now()}@example.test`, displayName: 'Departure member' };
      const registered = await member.request.post('/auth/register', { headers, data: credentials }); expect(registered.status()).toBe(201);
      const user = (await registered.json()).user.id;
      expect((await member.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: credentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const departure = await member.newPage(); await departure.goto(`/app/${org}/leave`);
      let writes = 0;
      departure.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname === `/organizations/${org}/leave`) writes++; });
      await departure.getByRole('button', { name: 'Review departure' }).focus(); await departure.keyboard.press('Enter');
      await expect(departure.getByRole('button', { name: 'Cancel departure' })).toBeFocused();
      expect((await new AxeBuilder({ page: departure }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await departure.keyboard.press('Enter'); await expect(departure.getByRole('dialog')).toHaveCount(0); expect(writes).toBe(0);
      await departure.getByRole('button', { name: 'Review departure' }).focus(); await departure.keyboard.press('Enter');
      await departure.getByRole('button', { name: 'Confirm departure' }).focus(); await departure.keyboard.press('Enter');
      await expect(departure.getByText('You left the Organization.')).toBeVisible(); expect(writes).toBe(1);
      expect((await member.request.get(`/organizations/${org}`)).status()).toBe(404);
      const directory = await context.request.get(`/organizations/${org}/members`); expect(directory.status()).toBe(200);
      expect((await directory.json()).items.some((item: { userId: string }) => item.userId === user)).toBe(false);
      await expect(departure.getByRole('status')).toBeFocused();
      async function rejoin() {
        const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
          data: { email: credentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
        expect(invitation.status()).toBe(201);
        expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      }
      await rejoin(); await departure.goto(`/app/${org}/leave`);
      const keys: string[] = []; const bodies: string[] = [];
      await departure.route(`**/organizations/${org}/leave`, async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        keys.push(route.request().headers()['idempotency-key']); bodies.push(route.request().postData()!);
        const response = await route.fetch(); expect(response.status()).toBe(204);
        if (keys.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
      });
      await departure.getByRole('button', { name: 'Review departure' }).focus(); await departure.keyboard.press('Enter');
      await departure.getByRole('button', { name: 'Confirm departure' }).focus(); await departure.keyboard.press('Enter');
      await expect(departure.getByText(/Your departure could not be confirmed/)).toBeVisible();
      await expect(departure.getByRole('button', { name: 'Review current membership' })).toBeDisabled();
      await rejoin();
      const beforeReplay = await context.request.get(`/organizations/${org}/members`); expect(beforeReplay.status()).toBe(200);
      const restored = (await beforeReplay.json()).items.find((item: { userId: string }) => item.userId === user);
      expect(restored).toBeDefined();
      await departure.getByRole('button', { name: 'Retry original departure' }).focus(); await departure.keyboard.press('Enter');
      await expect(departure.getByText('Original departure acknowledged. Review current membership to check later access.')).toBeVisible();
      expect(keys).toHaveLength(2); expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
      expect(keys[1]).toBe(keys[0]); expect(bodies[1]).toBe(bodies[0]);
      const afterReplay = await context.request.get(`/organizations/${org}/members`); expect(afterReplay.status()).toBe(200);
      expect((await afterReplay.json()).items.find((item: { userId: string }) => item.userId === user)).toEqual(restored);
      expect((await member.request.get(`/organizations/${org}`)).status()).toBe(200);
      await departure.getByRole('button', { name: 'Review current membership' }).focus(); await departure.keyboard.press('Enter');
      await expect(departure.getByRole('button', { name: 'Review departure' })).toBeEnabled();
      expect(keys).toHaveLength(2);
    } finally { await member.close(); }
  });
}
