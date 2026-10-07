import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03/60-TC-05/09/12: recipient socket refuses cookie substitution after original account capture at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${width}-${Date.now()}`;
    const original = { email: `recipient-bootstrap-${suffix}@example.test`, password: 'recipient-bootstrap-correct-horse', displayName: 'Original recipient' };
    const replacement = { ...original, email: `recipient-bootstrap-other-${suffix}@example.test`, displayName: 'Replacement recipient' };
    const registration = await context.request.post('/auth/register', { headers, data: original });
    expect(registration.status()).toBe(201); const actor = (await registration.json()).user.id;
    expect((await context.request.post('/auth/register', { headers, data: replacement })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: original })).status()).toBe(200);
    expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
    let changed = false, socketOpened = false, streamItems = 0, invitationReads = 0;
    page.on('request', request => { if (new URL(request.url()).pathname === '/me/invitations') invitationReads++; });
    page.on('websocket', socket => {
      const url = new URL(socket.url()); if (!url.pathname.startsWith('/invitations/live')) return;
      socketOpened = true; expect(url.searchParams.get('expectedActorId')).toBe(actor);
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string') return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) if (JSON.parse(part).type === 2) streamItems++;
      });
    });
    await page.route(url => url.pathname === '/me', async route => {
      const response = await route.fetch();
      if (!changed) {
        expect(response.status()).toBe(200); expect((await response.json()).id).toBe(actor);
        changed = true;
        // Keep the real original profile response, but replace the actual
        // cookie before the subsequent socket captures its session.
        expect((await context.request.post('/auth/login', { headers, data: replacement })).status()).toBe(200);
      }
      await route.fulfill({ response }).catch(() => {});
    });
    await page.goto('/app/invitations');
    await expect(page).toHaveURL(/\/login(?:\?|$)/, { timeout: 30_000 });
    expect(changed).toBe(true); expect(socketOpened).toBe(true); expect(streamItems).toBe(0); expect(invitationReads).toBe(0);
    await expect(page.getByRole('button', { name: /^Accept invitation to/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Retry invitation acceptance' })).toHaveCount(0);
  });
}

for (const width of [1280, 390]) {
  test(`PRD-03/60-TC-06/09/11/12: recipient retries failed account admission and restores actual live discovery at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${width}-${Date.now()}`;
    const recipient = { email: `recipient-bootstrap-retry-${suffix}@example.test`, password: 'recipient-bootstrap-correct-horse', displayName: 'Retry recipient' };
    const owner = { ...recipient, email: `recipient-bootstrap-retry-owner-${suffix}@example.test`, displayName: 'Retry issuer' };
    let fail = true, sockets = 0, documents = 0;
    try {
      expect((await issuer.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await issuer.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      const registration = await context.request.post('/auth/register', { headers, data: recipient });
      expect(registration.status()).toBe(201); const actor = (await registration.json()).user.id;
      expect((await context.request.post('/auth/login', { headers, data: recipient })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const organization = await issuer.request.post('/organizations', { headers, data: { name: 'Recovered live Portal scope' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      page.on('websocket', socket => {
        const url = new URL(socket.url()); if (!url.pathname.startsWith('/invitations/live')) return;
        sockets++; expect(url.searchParams.get('expectedActorId')).toBe(actor);
      });
      await page.route(url => url.pathname === '/me', async route => {
        if (fail) { fail = false; await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"session_unavailable"}' }); }
        else await route.continue();
      });
      await page.goto('/app/invitations');
      await expect(page.getByText('Unable to confirm the reviewed account. Refresh invitations before continuing.', { exact: true })).toBeVisible();
      expect(sockets).toBe(0); const refresh = page.getByRole('button', { name: 'Refresh invitations' });
      await refresh.focus(); await refresh.press('Enter');
      await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
      expect(sockets).toBe(1); await expect(refresh).toBeFocused();
      const issued = await issuer.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: recipient.email, surface: 'PORTAL', targetRole: 'OWNER' } });
      expect(issued.status()).toBe(201);
      await expect(page.getByRole('button', { name: 'Accept invitation to Recovered live Portal scope' })).toBeEnabled({ timeout: 15_000 });
      await expect(refresh).toBeFocused(); expect(sockets).toBe(1); expect(documents).toBe(1);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
    } finally { await issuer.close(); }
  });
}
