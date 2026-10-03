import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-14: attachment archive, exact recovery, restore and confirmed deletion at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `attachment-lifecycle-${width}-${Date.now()}@example.test`, password: 'attachment-browser-fixture-battery', displayName: 'Attachment administrator' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Lifecycle Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Lifecycle Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Lifecycle List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Attachment history' } })).json()).id;
    const path = `/cards/${card}/attachments`;
    const created = await context.request.post(`${path}/url`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Retained reference', url: 'https://example.test/retained', cardVersion: 1 } });
    expect(created.status()).toBe(200);
    const active = await (await context.request.get(path)).json(); expect(active.cardVersion).toBe(2); expect(active.items).toHaveLength(1);
    const attachment = active.items[0]; const route = `/app/${org}/boards/${board}/cards/${card}`;
    await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage(); await peer.goto(route);
      const show = peer.getByRole('button', { name: 'Show attachments', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      const link = peer.getByRole('link', { name: 'Retained reference (opens in a new tab)', exact: true }); await expect(link).toBeVisible();
      const manage = page.getByRole('button', { name: 'Manage attachments', exact: true });
      const archive = page.getByRole('button', { name: 'Archive attachment Retained reference', exact: true });
      const confirmArchive = page.getByRole('button', { name: 'Confirm attachment archive', exact: true });
      await expect(manage).toBeEnabled(); await manage.press('Enter'); await expect(archive).toBeEnabled(); await archive.press('Enter');
      const attempts: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**${path}/${attachment.id}/archive`, async intercepted => {
        const request = intercepted.request(); attempts.push({ key: request.headers()['idempotency-key'], body: request.postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (attempts.length === 1) await intercepted.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
        else await intercepted.fulfill({ response });
      });
      await expect(confirmArchive).toBeEnabled(); await confirmArchive.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original attachment change', exact: true });
      await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled();
      await expect(link).toHaveCount(0, { timeout: 20_000 });
      await expect(retry).toBeEnabled(); await retry.press('Enter');
      await expect(page.getByText('Attachment archived.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ cardVersion: 2, version: 1 }); expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      await page.unroute(`**${path}/${attachment.id}/archive`);
      await expect(manage).toBeEnabled(); await manage.press('Enter');
      const reviewArchive = page.getByRole('button', { name: 'Review attachment archive', exact: true });
      await expect(reviewArchive).toBeEnabled(); await reviewArchive.press('Enter');
      const restore = page.getByRole('button', { name: 'Restore attachment Retained reference', exact: true }); await expect(restore).toBeEnabled(); await restore.press('Enter');
      const confirmRestore = page.getByRole('button', { name: 'Confirm attachment restore', exact: true }); await expect(confirmRestore).toBeEnabled(); await confirmRestore.press('Enter');
      await expect(page.getByText('Attachment restored.', { exact: true })).toBeVisible(); await expect(link).toBeVisible({ timeout: 20_000 });
      const restored = await (await context.request.get(path)).json(); expect(restored.cardVersion).toBe(4); expect(restored.items[0].version).toBe(3); expect(restored.items[0].archivedAt).not.toBeNull();
      await expect(manage).toBeEnabled(); await manage.press('Enter'); await expect(archive).toBeEnabled(); await archive.press('Enter');
      await expect(confirmArchive).toBeEnabled(); await confirmArchive.press('Enter'); await expect(page.getByText('Attachment archived.', { exact: true })).toBeVisible();
      await expect(link).toHaveCount(0, { timeout: 20_000 });
      await expect(manage).toBeEnabled(); await manage.press('Enter'); await expect(reviewArchive).toBeEnabled(); await reviewArchive.press('Enter');
      const remove = page.getByRole('button', { name: 'Delete attachment Retained reference', exact: true }); await expect(remove).toBeEnabled(); await remove.press('Enter');
      const confirmed = page.getByRole('button', { name: 'Permanently delete attachment', exact: true }); await expect(confirmed).toBeDisabled();
      const consent = page.getByRole('checkbox', { name: 'I understand this attachment deletion cannot be undone', exact: true }); await expect(consent).toBeFocused(); await consent.press('Space');
      await expect(confirmed).toBeEnabled();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await confirmed.press('Enter'); await expect(page.getByText('Attachment permanently deleted.', { exact: true })).toBeVisible();
      const remaining = await (await context.request.get(path)).json(); expect(remaining.cardVersion).toBe(6); expect(remaining.items).toEqual([]);
      const retained = await (await context.request.get(`${path}/archive`)).json(); expect(retained.cardVersion).toBe(6); expect(retained.items).toEqual([]);
      expect((await context.request.post(`${path}/${attachment.id}/restore`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { cardVersion: 6, version: 5 } })).status()).toBe(404);
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await peerContext.close(); }
  });
}
