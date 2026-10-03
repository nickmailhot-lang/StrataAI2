import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';

// Actual native browser download against the immutable web image. File/scan
// provider responses are explicitly simulated; persisted file delivery has
// separate HTTP/PostgreSQL contracts and managed deployment remains unproven.
for (const width of [1280, 390]) {
  test(`PRD-14 native file download with reviewed current scope and simulated provider replies at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `download-client-${width}-${Date.now()}@example.test`, password: 'download-client-fixture-battery-horse', displayName: 'Download client owner' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Download client Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Download client Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Download client List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Native download review' } })).json()).id;
    const attachment = randomUUID(); const at = new Date().toISOString(); const bytes = Buffer.from('%PDF-1.7\nNative browser fixture.');
    const path = `/cards/${card}/attachments`;
    let downloaded = false;
    await page.route(`**${path}`, route => route.fulfill({ json: {
      organizationId: org, boardId: board, cardId: card, cardVersion: 1, canEdit: false, nextCursor: null,
      items: [{ id: attachment, organizationId: org, cardId: card, uploaderId: actor, kind: 0, displayName: 'Résumé.pdf',
        mimeType: 'application/pdf', sizeBytes: bytes.length, url: null, scanStatus: 2, scannedAt: at, createdAt: at, updatedAt: at, version: 2, deletedAt: null }]
    } }));
    await page.route(`**${path}/${attachment}/download-options`, route => route.fulfill({ json: {
      organizationId: org, boardId: board, cardId: card, cardVersion: 1, attachmentId: attachment, attachmentVersion: 2, actorId: actor
    } }));
    await page.route(`**${path}/${attachment}/download?*`, route => {
      const request = new URL(route.request().url()); expect(request.searchParams.get('actorId')).toBe(actor);
      expect(request.searchParams.get('attachmentVersion')).toBe('2'); downloaded = true;
      return route.fulfill({ headers: { 'Content-Type': 'application/octet-stream', 'Content-Disposition': 'attachment; filename="native-client-proof.pdf"',
        'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff' }, body: bytes });
    });
    await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
    await page.getByRole('button', { name: 'Show attachments', exact: true }).press('Enter');
    const review = page.getByRole('button', { name: 'Check file download access', exact: true }); await expect(review).toBeEnabled();
    expect(downloaded).toBe(false); await review.press('Enter');
    const link = page.getByRole('link', { name: 'Download Résumé.pdf (opens in a new tab)', exact: true });
    await expect(link).toBeFocused(); await expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    const native = page.waitForEvent('download'); await link.press('Enter'); const result = await native;
    expect(await result.failure()).toBeNull(); expect(result.suggestedFilename()).toBe('native-client-proof.pdf');
    const stream = await result.createReadStream(); expect(stream).not.toBeNull(); const chunks: Buffer[] = [];
    for await (const chunk of stream!) chunks.push(Buffer.from(chunk)); expect(Buffer.concat(chunks).equals(bytes)).toBe(true);
    expect(downloaded).toBe(true); await expect(page.getByText('Download requested. Your browser will report whether it completes.', { exact: true })).toBeVisible();
    const actual = await (await context.request.get(path)).json(); expect(actual.cardVersion).toBe(1); expect(actual.items).toEqual([]);
  });
}
