import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// This proves native archive controls against the immutable release web image.
// Metadata/publication/bytes are explicitly simulated. Real archive delivery
// authorization, integrity and withdrawal have separate API/PostgreSQL contracts.
for (const width of [1280, 390]) {
  test(`PRD-14 protected archive preview keyboard review with simulated delivery at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `archive-preview-${width}-${Date.now()}@example.test`, password: 'archive-preview-fixture-battery-horse', displayName: 'Archive reviewer' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Archive review Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Archive review Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Archive review List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Protected archive review' } })).json()).id;
    const attachment = randomUUID(); const at = new Date().toISOString(); const path = `/cards/${card}/attachments`;
    const file = { id: attachment, organizationId: org, cardId: card, uploaderId: actor, kind: 0, displayName: 'Retained.webp',
      mimeType: 'image/webp', sizeBytes: 1000, url: null, scanStatus: 2, scannedAt: at, createdAt: at, updatedAt: at,
      version: 4, lifecycleState: 1, archivedAt: at, deletedAt: null, deletedBy: null };
    await page.route(`**${path}`, route => route.fulfill({ json: { organizationId: org, boardId: board, cardId: card, cardVersion: 1, items: [], nextCursor: null, canEdit: false } }));
    await page.route(`**${path}/archive`, route => route.fulfill({ json: { organizationId: org, boardId: board, cardId: card, cardVersion: 1, items: [file], nextCursor: null, canRestore: false, canDelete: false } }));
    const archivePath = `${path}/archive/${attachment}`; let images = 0; let unavailable = false;
    await page.route(`**${archivePath}/*-options`, route => unavailable ? route.fulfill({ status: 404, json: { code: 'card_not_found' } })
      : route.fulfill({ json: { organizationId: org, boardId: board, cardId: card, cardVersion: 1, attachmentId: attachment, attachmentVersion: 4, actorId: actor } }));
    await page.route(`**${archivePath}/preview?*`, route => {
      const query = new URL(route.request().url()).searchParams;
      expect(query.get('actorId')).toBe(actor); expect(query.get('attachmentVersion')).toBe('4'); images++;
      return route.fulfill({ headers: { 'Content-Type': 'image/png', 'Cache-Control': 'private, no-store' },
        body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64') });
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`; const reads = trackBoardReads(page, board, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const manage = page.getByRole('button', { name: 'Manage attachments', exact: true }); await expect(manage).toBeEnabled(); await manage.press('Enter');
      const archive = page.getByRole('button', { name: 'Review attachment archive', exact: true }); await expect(archive).toBeEnabled(); await archive.press('Enter');
      const review = page.getByRole('button', { name: 'Show archived image preview', exact: true }); await expect(review).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Restore attachment Retained.webp', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Delete attachment Retained.webp', exact: true })).toBeDisabled(); expect(images).toBe(0);
      await review.press('Enter'); const image = page.getByRole('img', { name: 'Sanitized preview of Retained.webp', exact: true });
      await expect(image).toBeVisible(); await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(review).toBeFocused(); expect(images).toBe(1);
      const download = page.getByRole('button', { name: 'Check archived file download access', exact: true }); await expect(download).toBeEnabled(); await download.press('Enter');
      const link = page.getByRole('link', { name: 'Download archived Retained.webp (opens in a new tab)', exact: true }); await expect(link).toBeFocused();
      await expect(link).toHaveAttribute('href', `${archivePath}/download?actorId=${actor}&attachmentVersion=4`); await expect(link).toHaveAttribute('rel', 'noopener noreferrer');
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.getByRole('button', { name: 'Hide image preview', exact: true }).press('Enter'); await expect(image).toHaveCount(0); await expect(review).toBeFocused();
      unavailable = true; await expect(review).toBeEnabled(); await review.press('Enter'); await expect(page.getByRole('alert')).toContainText('Image preview is unavailable.');
      expect(images).toBe(1); await expect(image).toHaveCount(0);
      const actual = await (await context.request.get(`${path}/archive`)).json(); expect(actual.items).toEqual([]);
    } finally { restoreWorker(); }
  });
}
