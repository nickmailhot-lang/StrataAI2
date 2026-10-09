import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// Native client contract with simulated publication, receipt and PNG delivery.
// Genuine restricted PostgreSQL/Worker publication and HTTP ownership are
// separate mandatory contracts. This does not claim end-to-end publication.
for (const width of [1280, 390]) {
  test(`PRD-04 native stored background consent, original recovery and rendering at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `background-client-${width}-${Date.now()}@example.test`, password: 'background-client-correct-horse', displayName: 'Background owner' };
    await registerVerifiedAccountFixture(context.request, credentials);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Background client Organization' } })).json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Background Board', visibility: 'PUBLIC' } });
    expect(created.status()).toBe(201); const original = await created.json(); const board = original.id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Images' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Image source' } })).json()).id;
    const attachment = randomUUID(), owned = randomUUID(); let selected = false; let reads = 0;
    const attempts: { body: string | null; key: string | undefined }[] = [];
    await page.route(`**/boards/${board}`, async route => {
      const actual = await route.fetch(); expect(actual.status()).toBe(200); const snapshot = await actual.json();
      if (selected) snapshot.board = { ...snapshot.board, version: 2, backgroundType: 'IMAGE', backgroundValue: owned };
      await route.fulfill({ response: actual, json: snapshot });
    });
    await page.route(`**/cards/${card}/cover/candidates`, route => route.fulfill({ json: {
      organizationId: org, boardId: board, cardId: card, cardVersion: 1, canEdit: true, isPublic: true,
      items: [{ attachmentId: attachment, attachmentVersion: 3, displayName: 'Checked background.png', createdAt: '2026-10-03T08:00:00.123456Z' }], nextCursor: null,
    } }));
    await page.route(`**/boards/${board}/background/image`, async route => {
      expect(route.request().method()).toBe('POST');
      attempts.push({ body: route.request().postData(), key: route.request().headers()['idempotency-key'] }); selected = true;
      if (attempts.length === 1) return route.fulfill({ status: 503, json: { code: 'work_storage_unavailable' } });
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ cardId: card, attachmentId: attachment, attachmentVersion: 3, boardVersion: 1, publicVisibilityConfirmed: true });
      expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      return route.fulfill({ json: { ...original, version: 2, backgroundType: 'IMAGE', backgroundValue: owned } });
    });
    await page.route(`**/boards/${board}/background/image?*`, route => {
      expect(selected).toBe(true); expect(new URL(route.request().url()).searchParams.get('boardVersion')).toBe('2'); reads++;
      return route.fulfill({ headers: { 'Content-Type': 'image/png', 'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff' },
        body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64') });
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const path = `/app/${org}/boards/${board}/cards/${card}`; const tracked = trackBoardReads(page, board, path);
      await page.goto(path); await expect.poll(tracked).toBeGreaterThanOrEqual(2); expect(reads).toBe(0);
      const review = page.getByRole('button', { name: 'Review Board background images', exact: true });
      await expect(review).toBeEnabled(); await review.press('Enter');
      await page.getByRole('button', { name: 'Use Checked background.png as Board background', exact: true }).press('Enter');
      const save = page.getByRole('button', { name: 'Confirm Board background image', exact: true }); await expect(save).toBeDisabled();
      const consent = page.getByRole('checkbox', { name: 'I understand this Board background image will be publicly visible', exact: true });
      await expect(consent).toBeFocused(); await consent.press('Space'); await expect(save).toBeEnabled(); await save.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original Board background change', exact: true });
      await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      for (const name of ['Save card', 'Add checklist', 'Review Card cover', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
      await retry.press('Enter'); await expect(page.getByText('Board background updated.', { exact: true })).toBeVisible();
      const image = page.locator(`img[src="/boards/${board}/background/image?boardVersion=2"]`);
      await expect(image).toHaveCount(1); await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(image).toHaveAttribute('alt', ''); await expect(image).toHaveAttribute('aria-hidden', 'true');
      await expect(review).toBeFocused(); expect(reads).toBeGreaterThan(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      // Enforce the fixture's limitation: no simulated publication changed storage.
      const actual = await (await context.request.get(`/boards/${board}`)).json();
      expect(actual.board).toMatchObject({ version: 1, backgroundType: 'COLOR', backgroundValue: null });
    } finally { restoreWorker(); }
  });
}
