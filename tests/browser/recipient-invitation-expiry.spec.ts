import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const surface of ['INTERNAL', 'PORTAL', 'BOARD']) {
  test(`PRD-03/05/60: recipient ${surface} expiry and original-ID recovery at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }, suffix = `${width}-${surface}-${Date.now()}`;
    const recipient = { email: `expiry-recipient-${suffix}@example.test`, password: 'recipient-expiry-correct-horse', displayName: 'Expiry recipient' };
    try {
      const owner = { ...recipient, email: `expiry-issuer-${suffix}@example.test`, displayName: 'Expiry issuer' };
      expect((await issuer.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await issuer.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      expect((await context.request.post('/auth/register', { headers, data: recipient })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: recipient })).status()).toBe(200);
      const created = await issuer.request.post('/organizations', { headers, data: { name: 'Recipient expiry scope' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      let root = `/organizations/${org}/invitations`, board: string | undefined;
      if (surface === 'BOARD') {
        const enrollment = await issuer.request.post(root, { headers, data: { email: recipient.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
        expect(enrollment.status()).toBe(201);
        expect((await context.request.post(`/me/invitations/${(await enrollment.json()).id}/accept`, { headers })).status()).toBe(200);
        const result = await issuer.request.post('/boards', { headers, data: { organizationId: org, name: 'Recipient expiry Board' } });
        expect(result.status()).toBe(201); board = (await result.json()).id; root = `/boards/${board}/invitations`;
      }
      const issued = await issuer.request.post(root, { headers, data: board ? { email: recipient.email, role: 'MEMBER' }
        : { email: recipient.email, surface, targetRole: surface === 'PORTAL' ? 'OWNER' : 'MEMBER' } });
      expect(issued.status()).toBe(201); const id = (await issued.json()).id;
      const history = await issuer.request.get(root); expect(history.status()).toBe(200); const before = await history.json();
      const expires = Date.parse(before.items.find((row: { id: string }) => row.id === id).expiresAt);
      // Only the browser clock advances. The real API owns grant/expiry checks;
      // no SQL changes timestamps, membership, audits or event readiness.
      await page.clock.install({ time: new Date(expires - 2000) }); await page.clock.pauseAt(new Date(expires - 1000));
      let reads = 0, documents = 0, acceptedSource = false; const writes: string[] = []; let acknowledgment: unknown;
      page.on('websocket', socket => {
        if (!new URL(socket.url()).pathname.startsWith('/invitations/live')) return;
        socket.on('framereceived', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const part of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(part);
            if (message.type === 2 && message.item?.events?.some((event: { eventType: string }) => event.eventType === 'INVITATION_ACCEPTED'))
              acceptedSource = true;
          }
        });
      });
      page.on('request', request => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
        if (request.method() === 'GET' && new URL(request.url()).pathname === '/me/invitations') reads++;
      });
      await page.route(url => url.pathname === `/me/invitations/${id}/accept`, async route => {
        writes.push(route.request().url()); const response = await route.fetch(); expect(response.status()).toBe(200);
        if (writes.length === 1) { acknowledgment = await response.json(); await route.abort('timedout'); }
        else { expect(await response.json()).toEqual(acknowledgment); await route.fulfill({ response }); }
      });
      await page.goto('/app/invitations');
      const accept = page.getByRole('button', { name: /^Accept invitation to Recipient expiry scope/ });
      await expect(accept).toBeVisible(); const initialReads = reads;
      await page.clock.runFor(1500);
      await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
      await expect(accept).toHaveCount(0); await expect(page.getByRole('heading', { name: 'Recipient expiry scope', exact: true })).toHaveCount(0);
      expect(reads).toBeGreaterThan(initialReads); expect(writes).toHaveLength(0);
      const unchanged = await issuer.request.get(root); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(before);
      await page.clock.setSystemTime(new Date(expires - 1000)); await page.getByRole('button', { name: 'Refresh invitations' }).click();
      await expect(accept).toBeVisible(); await accept.focus(); await accept.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry invitation acceptance' }); await expect(retry).toBeVisible();
      await page.clock.runFor(1500); await expect(retry).toBeEnabled(); expect(writes).toHaveLength(1);
      await page.getByRole('button', { name: 'Refresh invitations' }).click(); await expect(retry).toBeEnabled();
      await expect(page.getByRole('link', { name: /^Open / })).toHaveCount(0);
      const committed = await issuer.request.get(root); expect(committed.status()).toBe(200); const after = await committed.json();
      expect(after.items.find((row: { id: string; acceptedAt: string | null }) => row.id === id).acceptedAt).not.toBeNull();
      // Delivery uses real server time. Drain the actual committed acceptance
      // invalidation before reviewing its original-ID recovery; otherwise that
      // source can correctly cancel the retry's acknowledgment mid-request.
      await expect.poll(() => acceptedSource, { timeout: 15_000 }).toBe(true);
      await page.clock.runFor(250); await expect(retry).toBeEnabled();
      await retry.focus(); await retry.press('Enter');
      await expect(page.getByRole('link', { name: surface === 'PORTAL' ? 'Open Owner Portal' : board ? 'Open Board' : 'Open organization', exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[0]).toBe(writes[1]); expect(documents).toBe(1);
      const recovered = await issuer.request.get(root); expect(recovered.status()).toBe(200); expect(await recovered.json()).toEqual(after);
      // Axe schedules browser timers. Resume only after every expiry, privacy
      // and original-command assertion so the accessibility scan can complete.
      await page.clock.resume();
      expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { await issuer.close(); }
  });
}
