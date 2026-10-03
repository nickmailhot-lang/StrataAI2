import AxeBuilder from '@axe-core/playwright';
import { createHash, randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';

// Native browser/file/keyboard client proof against the exact web image.
// File-provider replies are simulated; this does not claim S3 publication.
for (const width of [1280, 390]) {
  test(`PRD-14 file client: native bytes, original retry, quarantine disclosure and accessibility with simulated provider replies at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `file-client-${width}-${Date.now()}@example.test`, password: 'file-client-fixture-correct-horse', displayName: 'File client owner' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'File client Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'File client Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'File client List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Native file review' } })).json()).id;
    const scope = { organizationId: org, boardId: board, cardId: card }; const path = `/cards/${card}/attachments`;
    const bytes = Buffer.from('%PDF-1.7\n'); const at = new Date().toISOString();
    const attachment = { id: randomUUID(), organizationId: org, cardId: card, uploaderId: (await (await context.request.get('/me')).json()).id,
      kind: 0, displayName: 'Résumé.png', mimeType: 'application/pdf', sizeBytes: bytes.length, url: null, scanStatus: 1, scannedAt: null,
      createdAt: at, updatedAt: at, version: 1, deletedAt: null };
    const attempts: { headers: Record<string, string>; bytes: Buffer }[] = [];
    await page.route(`**/cards/${card}/attachment-upload-options`, route => route.fulfill({ json: { ...scope, cardVersion: 1,
      maximumBytes: 20971520, allowedMimeTypes: ['application/pdf'] } }));
    await page.route(`**/boards/${board}`, async route => {
      const upstream = await route.fetch(); expect(upstream.status()).toBe(200); const snapshot = await upstream.json();
      if (attempts.length) for (const column of snapshot.lists) for (const row of column.cards) if (row.id === card) row.version = 2;
      await route.fulfill({ json: snapshot });
    });
    await page.route(`**${path}`, async route => {
      if (route.request().method() !== 'POST') {
        await route.fulfill({ json: { ...scope, cardVersion: 2, canEdit: true, items: [attachment], nextCursor: null } }); return;
      }
      const request = route.request(); attempts.push({ headers: request.headers(), bytes: request.postDataBuffer()! });
      if (attempts.length === 1) await route.fulfill({ status: 503, json: { code: 'work_storage_unavailable', detail: 'Never expose this provider diagnostic.' } });
      else await route.fulfill({ json: { ...scope, cardVersion: 2, attachment } });
    });
    await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
    const add = page.getByRole('button', { name: 'Add file attachment', exact: true }); await expect(add).toBeEnabled(); await add.press('Enter');
    const file = page.getByLabel('File to attach', { exact: true }); await expect(file).toBeEnabled(); await expect(file).toBeFocused();
    await file.setInputFiles({ name: 'Résumé.png', mimeType: 'image/png', buffer: bytes });
    await page.getByRole('button', { name: 'Upload selected file', exact: true }).press('Enter');
    const retry = page.getByRole('button', { name: 'Retry original file upload', exact: true }); await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
    await expect(file).toBeDisabled(); await expect(page.getByRole('button', { name: 'Add link attachment', exact: true })).toBeDisabled();
    expect(attempts).toHaveLength(1); expect(attempts[0].bytes.equals(bytes)).toBe(true);
    expect(attempts[0].headers['x-strataai-request']).toBe('1'); expect(attempts[0].headers['content-type']).toBe('application/octet-stream');
    expect(attempts[0].headers['x-attachment-sha256']).toBe(createHash('sha256').update(bytes).digest('hex'));
    expect(Buffer.from(attempts[0].headers['x-attachment-name'], 'base64').toString('utf8')).toBe('Résumé.png');
    expect(attempts[0].headers['x-attachment-size']).toBe(String(bytes.length)); expect(attempts[0].headers['x-card-version']).toBe('1');
    expect(attempts[0].headers['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
    await retry.press('Enter'); await expect(page.getByText('File attached. Safety scan pending.', { exact: true })).toBeVisible();
    await expect(add).toBeEnabled(); await expect(add).toBeFocused(); expect(attempts).toHaveLength(2);
    expect(attempts[1].bytes.equals(attempts[0].bytes)).toBe(true);
    for (const name of ['idempotency-key', 'x-attachment-name', 'x-attachment-size', 'x-attachment-sha256', 'x-card-version'])
      expect(attempts[1].headers[name]).toBe(attempts[0].headers[name]);
    await page.getByRole('button', { name: 'Show attachments', exact: true }).press('Enter');
    await expect(page.getByText('Safety scan pending. File access is unavailable.', { exact: true })).toBeVisible();
    await expect(page.getByText('Résumé.png (9 bytes)', { exact: true })).toBeVisible();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    // The simulated file replies must not be misreported as persisted files.
    const actual = await (await context.request.get(path)).json(); expect(actual.cardVersion).toBe(1); expect(actual.items).toEqual([]);
  });
}
