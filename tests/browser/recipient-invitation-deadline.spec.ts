import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03/60-TC-06/07/11/12: recipient whole deadline retains original committed Portal acceptance at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${width}-${Date.now()}`;
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let releaseProfile!: () => void, releaseCommand!: () => void;
    const profileGate = new Promise<void>(resolve => { releaseProfile = resolve; });
    const commandGate = new Promise<void>(resolve => { releaseCommand = resolve; });
    let holdProfile = false, profileHeld = false, commandHeld = false, documents = 0;
    try {
      const owner = { email: `recipient-deadline-owner-${suffix}@example.test`, password: 'recipient-deadline-correct-horse', displayName: 'Deadline issuer' };
      const recipient = { ...owner, email: `recipient-deadline-${suffix}@example.test`, displayName: 'Deadline recipient' };
      expect((await issuer.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await issuer.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      const registration = await context.request.post('/auth/register', { headers, data: recipient });
      expect(registration.status()).toBe(201); const actor = (await registration.json()).user.id;
      expect((await context.request.post('/auth/login', { headers, data: recipient })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const organization = await issuer.request.post('/organizations', { headers, data: { name: 'Deadline Portal scope' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      const root = `/organizations/${org}/invitations`;
      const created = await issuer.request.post(root, { headers, data: { email: recipient.email, surface: 'PORTAL', targetRole: 'OWNER' } });
      expect(created.status()).toBe(201); const id = (await created.json()).id;
      const path = `/me/invitations/${id}/accept`; const writes: string[] = []; let original: unknown;
      await page.route(url => url.pathname === '/me', async route => {
        if (holdProfile) { holdProfile = false; profileHeld = true; await profileGate; }
        await route.continue().catch(() => {});
      });
      await page.route(url => url.pathname === path, async route => {
        expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(actor);
        writes.push(route.request().url()); const response = await route.fetch(); expect(response.status()).toBe(200);
        if (writes.length === 1) { original = await response.json(); commandHeld = true; await commandGate; }
        else expect(await response.json()).toEqual(original);
        await route.fulfill({ response }).catch(() => {});
      });
      page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      await page.goto('/app/invitations');
      const accept = page.getByRole('button', { name: 'Accept invitation to Deadline Portal scope' }); await expect(accept).toBeEnabled();
      await page.clock.install(); holdProfile = true; await accept.focus(); await accept.press('Enter');
      await expect.poll(() => profileHeld).toBe(true); await page.clock.runFor(8_000); releaseProfile();
      await expect.poll(() => commandHeld).toBe(true); expect(writes).toHaveLength(1);
      await page.clock.runFor(7_001); await page.clock.runFor(500);
      await expect(page.getByRole('link', { name: 'Open Owner Portal' })).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Deadline Portal scope', exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Refresh invitations' })).toBeEnabled();
      releaseCommand();
      await page.getByRole('button', { name: 'Refresh invitations' }).focus(); await page.keyboard.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry invitation acceptance' }); await expect(retry).toBeEnabled();
      expect(writes).toHaveLength(1); await retry.focus(); await retry.press('Enter');
      await expect(page.getByRole('link', { name: 'Open Owner Portal' })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toBe(writes[0]); expect(documents).toBe(1);
      const history = await issuer.request.get(root); expect(history.status()).toBe(200);
      const rows = (await history.json()).items.filter((row: { id: string }) => row.id === id);
      expect(rows).toHaveLength(1); expect(rows[0].acceptedAt).toBeTruthy();
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { holdProfile = false; releaseProfile(); releaseCommand(); await issuer.close(); }
  });
}
