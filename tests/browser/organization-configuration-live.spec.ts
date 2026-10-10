import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

// PRD-27 TC-06/09/11/12: real separate Worker, current sessions, source-only
// publication and keyboard review recovery through the release edge.
for (const width of [1280, 390]) {
  test(`PRD-27: private configuration realtime preserves unsent work and restores keyboard review at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    await registerNotificationAccount(context.request, { email: `configuration-live-${width}-${Date.now()}@example.test`,
      password: 'configuration-live-correct-horse', displayName: 'Configuration realtime owner' }, true);
    const headers = { 'X-StrataAI-Request': '1' };
    const created = await context.request.post('/organizations', { headers, data: { name: 'Configuration realtime acceptance' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
    const path = `/organizations/${organization}/configuration`;
    const data = { legalName: 'Initial private legal name', jurisdiction: 'CA-BC', timezone: 'UTC' };
    const first = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { version: 0, configuration: data } });
    expect(first.status()).toBe(200);
    const restoreWorker = scopedBoardWorker(organization);
    const peer = await browser.newContext({ storageState: await context.storageState() });
    const frames: Record<string, unknown>[] = [];
    page.on('websocket', socket => {
      if (!new URL(socket.url()).pathname.endsWith('/organizations/live/configuration')) return;
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string') return;
        for (const value of frame.payload.split('\u001e').filter(Boolean)) {
          try { frames.push(JSON.parse(value) as Record<string, unknown>); } catch { /* Handshake fragments carry no usable source. */ }
        }
      });
    });
    try {
      await page.goto(`/app/${organization}/configuration`);
      await expect.poll(() => frames.some(frame => frame.type === 2), { timeout: 10_000 }).toBe(true);
      await expect(page.getByTestId('configuration-authority')).toHaveAttribute('data-ready', 'true');
      await page.getByLabel(/^Legal name/).fill('Preserved keyboard proposal');
      const review = page.getByRole('button', { name: 'Review configuration change', exact: true });
      await review.focus(); await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Review configuration change', exact: true });
      await expect(dialog).toBeVisible();
      const changed = await peer.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 1, configuration: { ...data, legalName: 'Changed private legal name' } } });
      expect(changed.status()).toBe(200); const source = await changed.json();
      await expect(page.getByText('Current revision: 2', { exact: true })).toBeVisible({ timeout: 10_000 });
      await expect(dialog).not.toBeVisible();
      await expect(page.getByLabel(/^Legal name/)).toHaveValue('Preserved keyboard proposal');
      await expect(review).toBeFocused();
      await expect.poll(() => frames.some(frame => frame.type === 2
        && (frame.item as { page?: { events?: { eventId?: string }[] } } | undefined)?.page?.events?.some(event => event.eventId === source.eventId)),
      { timeout: 10_000 }).toBe(true);
      expect(JSON.stringify(frames)).not.toContain('Changed private legal name');
      expect(JSON.stringify(frames)).not.toContain('Preserved keyboard proposal');
      await page.keyboard.press('Enter'); await expect(dialog).toBeVisible();
      await expect(dialog.getByText('Changed private legal name', { exact: true })).toBeVisible();
      await expect(dialog.getByText('Preserved keyboard proposal', { exact: true })).toBeVisible();
      await page.keyboard.press('Tab'); await expect(dialog.getByRole('button', { name: 'Approve configuration change' })).toBeFocused();
      await page.keyboard.press('Enter'); await expect(dialog).not.toBeVisible();
      await expect(page.getByText('Change acknowledged. Current configuration revision 3.', { exact: true })).toBeVisible();
      await expect(review).toBeFocused();
      const activity = page.getByRole('region', { name: 'Configuration Activity', exact: true });
      const loadActivity = activity.getByRole('button', { name: 'Load configuration Activity' });
      await loadActivity.focus(); await page.keyboard.press('Enter');
      await expect(activity.getByRole('heading', { name: 'Configuration Activity', exact: true })).toBeFocused();
      await expect(activity.getByText('Configuration changed at revision 3', { exact: true })).toBeVisible();
      await expect(activity.getByText(`Original event reference: ${source.eventId}`, { exact: true })).toBeVisible();
      await expect(activity.locator('time').filter({ hasText: source.updatedAt })).toBeVisible();
      await expect(activity.getByText('Changed private legal name', { exact: true })).toHaveCount(0);
      await expect(activity.getByText('Preserved keyboard proposal', { exact: true })).toHaveCount(0);
      const logout = await peer.request.post('/auth/logout', { headers, data: {} }); expect(logout.status()).toBe(204);
      await expect(page).toHaveURL(/\/login$/);
      await expect(page.getByLabel(/^Legal name/)).toHaveCount(0);
      await expect(page.getByText('Preserved keyboard proposal', { exact: true })).toHaveCount(0);
      await expect(activity).toHaveCount(0);
    } finally { await peer.close(); restoreWorker(); }
  });
}
