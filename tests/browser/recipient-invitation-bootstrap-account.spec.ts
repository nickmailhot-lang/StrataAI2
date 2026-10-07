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
