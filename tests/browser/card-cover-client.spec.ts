import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// These are native client contract scenarios with explicitly simulated cover
// publication, receipts and PNG delivery. Actual PostgreSQL publication and
// current-viewer byte admission are separate contracts; this does not claim an
// upload-through-Worker-to-cover end-to-end test.
for (const width of [1280, 390]) {
  test('PRD-14 native PUBLIC cover consent, original retry and removal with simulated provider at ' + width + 'px', async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `cover-client-${width}-${Date.now()}@example.test`,
      password: 'cover-client-fixture-battery-horse', displayName: 'Cover client owner' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Cover client Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'PUBLIC cover Board', visibility: 'PUBLIC' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Cover List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Cover consent Card' } })).json()).id;
    const scope = { organizationId: org, boardId: board, cardId: card };
    const attachment = randomUUID(); let version = 1; let selected = false; let imageReads = 0;
    const writes: { key: string | undefined; body: string | null }[] = [];
    const cover = `/cards/${card}/cover`;
    await page.route(`**/boards/${board}`, async route => {
      const actual = await route.fetch(); expect(actual.status()).toBe(200);
      const snapshot = await actual.json();
      for (const column of snapshot.lists) for (const row of column.cards) if (row.id === card) {
        row.version = version; row.hasCover = selected;
      }
      await route.fulfill({ response: actual, json: snapshot });
    });
    await page.route('**' + cover, async route => {
      if (route.request().method() === 'GET') return route.fulfill({ json: { ...scope, cardVersion: version,
        attachmentId: selected ? attachment : null, attachmentVersion: selected ? 3 : null, canEdit: true, isPublic: true } });
      expect(route.request().method()).toBe('PUT');
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      if (writes.length === 1) {
        selected = true; version = 2;
        return route.fulfill({ status: 503, json: { code: 'work_storage_unavailable' } });
      }
      if (writes.length === 2) {
        expect(writes[1]).toEqual(writes[0]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/i);
        expect(JSON.parse(writes[0].body!)).toEqual({ attachmentId: attachment, attachmentVersion: 3, cardVersion: 1, publicVisibilityConfirmed: true });
        return route.fulfill({ json: { ...scope, cardVersion: 2, attachmentId: attachment, attachmentVersion: 3, changed: true } });
      }
      expect(writes).toHaveLength(3); expect(writes[2].key).not.toBe(writes[0].key);
      expect(JSON.parse(writes[2].body!)).toEqual({ attachmentId: null, attachmentVersion: null, cardVersion: 2, publicVisibilityConfirmed: false });
      selected = false; version = 3;
      return route.fulfill({ json: { ...scope, cardVersion: 3, attachmentId: null, attachmentVersion: null, changed: true } });
    });
    await page.route('**' + cover + '/candidates', route => route.fulfill({ json: { ...scope, cardVersion: version,
      items: [{ attachmentId: attachment, attachmentVersion: 3, displayName: 'Cover photo.png', createdAt: '2026-10-03T08:00:00.123456Z' }],
      nextCursor: null, canEdit: true, isPublic: true } }));
    await page.route('**' + cover + '/image?*', route => {
      expect(selected).toBe(true); expect(new URL(route.request().url()).searchParams.get('cardVersion')).toBe(String(version)); imageReads++;
      return route.fulfill({ headers: { 'Content-Type': 'image/png', 'Cache-Control': 'private, no-store', 'X-Content-Type-Options': 'nosniff' },
        body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64') });
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`; const reads = trackBoardReads(page, board, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      expect(imageReads).toBe(0);
      const review = page.getByRole('button', { name: 'Review Card cover', exact: true });
      await expect(review).toBeEnabled(); await review.press('Enter');
      await page.getByRole('button', { name: 'Use Cover photo.png as cover', exact: true }).press('Enter');
      const confirm = page.getByRole('button', { name: 'Confirm Card cover', exact: true }); await expect(confirm).toBeDisabled();
      const consent = page.getByRole('checkbox', { name: 'I understand this cover image will be publicly visible', exact: true });
      await expect(consent).toBeFocused(); await consent.press('Space'); await expect(confirm).toBeEnabled(); await confirm.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original cover change', exact: true });
      await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      for (const name of ['Add checklist', 'Save card', 'Add link attachment', 'Manage attachments', 'Close'])
        await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
      await retry.press('Enter'); await expect(page.getByText('Card cover updated.', { exact: true })).toBeVisible();
      const image = page.getByRole('img', { name: 'Card cover', exact: true }); await expect(image).toBeVisible();
      await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(review).toBeFocused();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await review.press('Enter'); await page.getByRole('button', { name: 'Remove Card cover', exact: true }).press('Enter');
      await expect(consent).toHaveCount(0); await page.getByRole('button', { name: 'Confirm cover removal', exact: true }).press('Enter');
      await expect(page.getByText('Card cover removed.', { exact: true })).toBeVisible(); await expect(image).toHaveCount(0);
      await expect(review).toBeEnabled(); await expect(review).toBeFocused(); expect(writes).toHaveLength(3); expect(imageReads).toBeGreaterThan(0);
      // The source responses were simulated: canonical backing Card was never
      // changed by them. Keep that limitation executable and explicit.
      const actual = await (await context.request.get(`/boards/${board}`)).json();
      expect(actual.lists.flatMap((column: { cards: { id: string; version: number; hasCover?: boolean }[] }) => column.cards)
        .find((row: { id: string }) => row.id === card)).toMatchObject({ version: 1, hasCover: false });
    } finally { restoreWorker(); }
  });
}
