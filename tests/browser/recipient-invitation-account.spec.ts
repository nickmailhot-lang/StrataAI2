import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const surface of ['INTERNAL', 'PORTAL', 'BOARD']) for (const fault of ['before', 'replacement', 'unavailable']) {
  test(`PRD-60-TC-06/07/11/12: recipient ${surface} acceptance rechecks account ${fault} at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${width}-${surface}-${fault}-${Date.now()}`;
    const original = { email: `recipient-reviewed-${suffix}@example.test`, password: 'recipient-account-correct-horse', displayName: 'Reviewed recipient' };
    const other = { ...original, email: `recipient-other-${suffix}@example.test`, displayName: 'Replacement recipient' };
    try {
      const owner = { ...original, email: `recipient-issuer-${suffix}@example.test`, displayName: 'Invitation issuer' };
      expect((await issuer.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await issuer.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      const registration = await context.request.post('/auth/register', { headers, data: original });
      expect(registration.status()).toBe(201); const actor = (await registration.json()).user.id;
      expect((await context.request.post('/auth/register', { headers, data: other })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: original })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await issuer.request.post('/organizations', { headers, data: { name: 'Recipient account scope' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      let root = `/organizations/${org}/invitations`; let board: string | undefined;
      if (surface === 'BOARD') {
        const enrollment = await issuer.request.post(root, { headers, data: { email: original.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
        expect(enrollment.status()).toBe(201);
        expect((await context.request.post(`/me/invitations/${(await enrollment.json()).id}/accept`, { headers })).status()).toBe(200);
        const creation = await issuer.request.post('/boards', { headers, data: { organizationId: org, name: 'Recipient private Board' } });
        expect(creation.status()).toBe(201); board = (await creation.json()).id; root = `/boards/${board}/invitations`;
      }
      const issued = await issuer.request.post(root, { headers, data: board ? { email: original.email, role: 'MEMBER' }
        : { email: original.email, surface, targetRole: surface === 'PORTAL' ? 'OWNER' : 'MEMBER' } });
      expect(issued.status()).toBe(201); const id = (await issued.json()).id;
      const path = `/me/invitations/${id}/accept`; const writes: string[] = [];
      let acknowledgment: unknown; let profileUnavailable = false;
      await page.route(url => url.pathname === '/me', async route => {
        if (!profileUnavailable) { await route.continue(); return; }
        profileUnavailable = false;
        await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"session_unavailable"}' });
      });
      await page.route(url => url.pathname === path, async route => {
        expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(actor);
        writes.push(route.request().url());
        const response = await route.fetch(); expect(response.status()).toBe(200);
        const ack = await response.json();
        if (writes.length === 1) {
          acknowledgment = ack;
          if (fault === 'replacement') expect((await context.request.post('/auth/login', { headers, data: other })).status()).toBe(200);
          else if (fault === 'unavailable') profileUnavailable = true;
        } else expect(ack).toEqual(acknowledgment);
        await route.fulfill({ response });
      });
      await page.goto('/app/invitations');
      const accept = page.getByRole('button', { name: /^Accept invitation to Recipient account scope/ });
      await expect(accept).toBeVisible();
      if (fault === 'before') expect((await context.request.post('/auth/login', { headers, data: other })).status()).toBe(200);
      await accept.focus(); await accept.press('Enter');
      if (fault !== 'unavailable') await expect(page).toHaveURL(/\/login(?:\?|$)/);
      else {
        await expect(page.getByText('Unable to confirm the reviewed account. Refresh invitations before continuing.', { exact: true })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Retry invitation acceptance' })).toBeDisabled();
      }
      await expect(page.getByRole('link', { name: /^Open / })).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Recipient account scope', exact: true })).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Recipient private Board', exact: true })).toHaveCount(0);
      expect(writes).toHaveLength(fault === 'before' ? 0 : 1);
      const history = await issuer.request.get(root); expect(history.status()).toBe(200);
      const row = (await history.json()).items.find((item: { id: string }) => item.id === id);
      if (fault === 'before') expect(row.acceptedAt).toBeNull(); else expect(row.acceptedAt).toBeTruthy();
      if (fault !== 'unavailable') {
        expect((await context.request.post('/auth/login', { headers, data: original })).status()).toBe(200);
        await page.goto('/app/invitations');
        if (fault === 'before') {
          await expect(accept).toBeVisible(); expect(writes).toHaveLength(0);
          await accept.focus(); await accept.press('Enter');
          await expect(page.getByRole('link', { name: surface === 'PORTAL' ? 'Open Owner Portal' : board ? 'Open Board' : 'Open organization', exact: true })).toBeVisible();
          expect(writes).toHaveLength(1);
        } else {
          await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
          expect(writes).toHaveLength(1);
        }
        const retry = await context.request.post(`${path}?expectedActorId=${actor}`, { headers });
        expect(retry.status()).toBe(200); expect(await retry.json()).toEqual(acknowledgment);
      } else {
        await page.getByRole('button', { name: 'Refresh invitations' }).focus(); await page.keyboard.press('Enter');
        const retry = page.getByRole('button', { name: 'Retry invitation acceptance' }); await expect(retry).toBeEnabled();
        expect(writes).toHaveLength(1); await retry.focus(); await retry.press('Enter');
        await expect(page.getByRole('link', { name: surface === 'PORTAL' ? 'Open Owner Portal' : board ? 'Open Board' : 'Open organization', exact: true })).toBeVisible();
        expect(writes).toHaveLength(2); expect(writes[1]).toBe(writes[0]);
      }
      const finalHistory = await issuer.request.get(root); expect(finalHistory.status()).toBe(200);
      const finalRow = (await finalHistory.json()).items.find((item: { id: string }) => item.id === id);
      if (fault === 'before') { expect(finalRow.acceptedAt).toBeTruthy(); expect(finalRow.id).toBe(row.id); }
      else expect(finalRow).toEqual(row);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await issuer.close(); }
  });
}
