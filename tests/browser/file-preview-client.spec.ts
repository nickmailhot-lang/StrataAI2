import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// Native image rendering against the exact release web image. Publication and
// provider replies are explicitly simulated here; restricted PostgreSQL and
// actual private HTTP integrity/authorization execute in separate contracts.
for (const width of [1280, 390]) {
  test('PRD-14 native sanitized image preview with simulated published provider replies at ' + width + 'px', async ({ page, context }) => {
    test.setTimeout(120000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: 'preview-client-' + width + '-' + Date.now() + '@example.test',
      password: 'preview-client-fixture-battery-horse', displayName: 'Preview client owner' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Preview client Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Preview client Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post('/boards/' + board + '/lists', { headers, data: { name: 'Preview client List' } })).json()).id;
    const card = (await (await context.request.post('/lists/' + list + '/cards', { headers, data: { title: 'Sanitized image preview' } })).json()).id;
    const attachment = randomUUID(); const at = new Date().toISOString();
    const path = '/cards/' + card + '/attachments';
    let images = 0; let unavailable = false;
    await page.route('**' + path, route => route.fulfill({ json: {
      organizationId: org, boardId: board, cardId: card, cardVersion: 1, canEdit: false, nextCursor: null,
      items: [{ id: attachment, organizationId: org, cardId: card, uploaderId: actor, kind: 0, displayName: 'Photo.webp',
        mimeType: 'image/webp', sizeBytes: 100003, url: null, scanStatus: 2, scannedAt: at, createdAt: at, updatedAt: at, version: 3, deletedAt: null }]
    } }));
    await page.route('**' + path + '/' + attachment + '/preview-options', route => unavailable
      ? route.fulfill({ status: 404, json: { code: 'card_not_found' } })
      : route.fulfill({ json: { organizationId: org, boardId: board, cardId: card, cardVersion: 1,
        attachmentId: attachment, attachmentVersion: 3, actorId: actor } }));
    await page.route('**' + path + '/' + attachment + '/preview?*', route => {
      const query = new URL(route.request().url()).searchParams;
      expect(query.get('actorId')).toBe(actor); expect(query.get('attachmentVersion')).toBe('3'); images++;
      return route.fulfill({ headers: { 'Content-Type': 'image/png', 'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff' },
        body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64') });
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const cardPath = '/app/' + org + '/boards/' + board + '/cards/' + card;
      const reads = trackBoardReads(page, board, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await page.getByRole('button', { name: 'Show attachments', exact: true }).press('Enter');
      const review = page.getByRole('button', { name: 'Show image preview', exact: true }); await expect(review).toBeEnabled();
      expect(images).toBe(0); await review.press('Enter');
      const image = page.getByRole('img', { name: 'Sanitized preview of Photo.webp', exact: true });
      await expect(image).toBeVisible(); await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(review).toBeFocused(); expect(images).toBe(1);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.getByRole('button', { name: 'Hide image preview', exact: true }).press('Enter');
      await expect(image).toHaveCount(0); await expect(review).toBeFocused();
      unavailable = true; await review.press('Enter');
      await expect(page.getByRole('alert')).toContainText('Image preview is unavailable.');
      await expect(image).toHaveCount(0); expect(images).toBe(1);
      await expect(review).toBeFocused();
      const actual = await (await context.request.get(path)).json(); expect(actual.cardVersion).toBe(1); expect(actual.items).toEqual([]);
    } finally { restoreWorker(); }
  });
}
