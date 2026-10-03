import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-14 URL attachments: keyboard creation, lost reply, original retry and safe disclosure at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const data = { email: `attachment-owner-${width}-${Date.now()}@example.test`, password: 'attachment-browser-fixture-battery', displayName: 'Attachment owner' };
    expect((await context.request.post('/auth/register', { headers, data })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'URL attachment Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'URL attachment Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'URL attachment List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Safe URL references' } })).json()).id;
    const path = `/cards/${card}/attachments`; const writes: { body: string; key: string | undefined }[] = [];
    await page.route(`**${path}/url`, async route => {
      const request = route.request(); writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'] });
      const result = await route.fetch(); expect(result.status()).toBe(200);
      if (writes.length === 1) await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
      else await route.fulfill({ response: result });
    });
    await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
    const add = page.getByRole('button', { name: 'Add link attachment', exact: true }); await expect(add).toBeEnabled(); await add.press('Enter');
    const title = page.getByRole('textbox', { name: 'New link attachment title', exact: true }); const url = page.getByRole('textbox', { name: 'Attachment URL', exact: true });
    await title.fill('External reference'); await url.fill('javascript:alert(1)');
    const create = page.getByRole('button', { name: 'Create link attachment', exact: true }); await expect(create).toBeEnabled(); await create.press('Enter');
    await expect(page.getByText('Enter an HTTP(S) link without embedded credentials.', { exact: true })).toBeVisible(); expect(writes).toHaveLength(0);
    await url.fill('https://example.test/reference?q=1#section'); await expect(create).toBeEnabled(); await create.press('Enter');
    const retry = page.getByRole('button', { name: 'Retry link attachment creation', exact: true }); await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
    await expect(title).toHaveValue('External reference'); await expect(url).toHaveValue('https://example.test/reference?q=1#section');
    await expect(title).toBeDisabled(); await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled(); expect(writes).toHaveLength(1);
    const committed = await context.request.get(path); expect(committed.status()).toBe(200);
    const first = await committed.json(); expect(first.cardVersion).toBe(2); expect(first.items).toHaveLength(1);
    await retry.press('Enter'); await expect(page.getByText('Link attachment created.', { exact: true })).toBeVisible();
    await expect(add).toBeEnabled(); await expect(add).toBeFocused();
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(JSON.parse(writes[0].body)).toEqual({ title: 'External reference', url: 'https://example.test/reference?q=1#section', cardVersion: 1 });
    expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
    const final = await context.request.get(path); expect(final.status()).toBe(200);
    const current = await final.json(); expect(current.cardVersion).toBe(2); expect(current.items).toHaveLength(1); expect(current.items[0].id).toBe(first.items[0].id);
    const show = page.getByRole('button', { name: 'Show attachments', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
    const link = page.getByRole('link', { name: 'External reference (opens in a new tab)', exact: true }); await expect(link).toBeVisible();
    await expect(link).toHaveAttribute('href', 'https://example.test/reference?q=1#section'); await expect(link).toHaveAttribute('target', '_blank');
    await expect(link).toHaveAttribute('rel', 'noopener noreferrer'); await expect(link).toHaveAttribute('referrerpolicy', 'no-referrer');
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.getByRole('button', { name: 'Hide attachments', exact: true }).press('Enter'); await expect(link).toHaveCount(0);
    await page.unroute(`**${path}/url`);
  });
}
