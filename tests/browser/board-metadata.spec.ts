import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-04: metadata save recovery, concurrent draft review and responsive background at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 }); await page.emulateMedia({ colorScheme: 'light' });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `board-metadata-${width}-${Date.now()}@example.test`, password: 'metadata-correct-horse-battery', displayName: 'Metadata editor' };
    await registerVerifiedAccountFixture(context.request, account);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Metadata browser fixture' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Original Board', visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201); const board = await created.json();
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board.id);
      const path = `/app/${org}/boards/${board.id}`; const other = await context.newPage();
      await other.setViewportSize({ width, height: 844 }); await other.emulateMedia({ colorScheme: 'light' });
      const reads = trackBoardReads(other, board.id, path);
      await page.goto(path); await other.goto(path);
      for (const client of [page, other]) await expect(client.getByText('Live updates connected.', { exact: true })).toBeVisible();
      await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const longName = 'W'.repeat(160); const description = 'First description line\nSecond description line';
      const requests: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/boards/${board.id}`, async route => {
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        requests.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const result = await route.fetch(); expect(result.status()).toBe(200);
        if (requests.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
      });
      await page.getByRole('button', { name: 'Edit Board details', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('textbox', { name: 'Board name', exact: true }).fill(longName);
      await page.getByRole('textbox', { name: 'Board description', exact: true }).fill(description);
      await page.getByRole('combobox', { name: 'Board background', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('option', { name: 'Purple', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Save Board details', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Retry this Board save', exact: true })).toBeEnabled();
      await expect(page.getByRole('textbox', { name: 'Board name', exact: true })).toHaveValue(longName);
      await expect(page.getByRole('textbox', { name: 'Board name', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Cancel Board changes', exact: true })).toHaveCount(0);
      await expect(other.getByRole('heading', { name: longName, exact: true })).toBeVisible();
      await expect(other.getByRole('region', { name: 'Board workspace', exact: true })).toHaveCSS('background-color', 'rgb(250, 245, 255)');
      await page.getByRole('button', { name: 'Retry this Board save', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Board changes acknowledged. Current Board state is being checked.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Edit Board details', exact: true })).toBeFocused();
      expect(requests).toHaveLength(2); expect(requests[1]).toEqual(requests[0]); expect(requests[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(JSON.parse(requests[0].body!)).toEqual({ name: longName, description, version: board.version, backgroundType: 'COLOR', backgroundValue: 'purple' });
      for (const client of [page, other]) {
        await expect(client.getByRole('heading', { name: longName, exact: true })).toBeVisible();
        await expect(client.getByText(description, { exact: true })).toHaveCSS('white-space', 'pre-wrap');
        expect(await client.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      }
      await page.getByRole('button', { name: 'Edit Board details', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('textbox', { name: 'Board name', exact: true }).fill('Retained draft');
      const concurrent = await context.request.patch(`/boards/${board.id}`, { headers, data: { name: 'Concurrent Board', description: 'Concurrent description', version: board.version + 1 } });
      expect(concurrent.status()).toBe(200);
      await expect(page.getByText('Current Board name: Concurrent Board', { exact: true })).toBeVisible();
      await expect(page.getByRole('textbox', { name: 'Board name', exact: true })).toHaveValue('Retained draft');
      await expect(page.getByRole('button', { name: 'Save Board details', exact: true })).toBeDisabled();
      await expect(other.getByRole('heading', { name: 'Concurrent Board', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Review current Board revision', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('textbox', { name: 'Board description', exact: true }).fill('Reviewed draft description');
      await page.getByRole('button', { name: 'Save Board details', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Edit Board details', exact: true })).toBeFocused();
      await expect(other.getByRole('heading', { name: 'Retained draft', exact: true })).toBeVisible();
      expect(requests).toHaveLength(3); expect(requests[2].key).not.toBe(requests[0].key);
      expect(JSON.parse(requests[2].body!)).toEqual({ name: 'Retained draft', description: 'Reviewed draft description', version: board.version + 2 });
      const final = await context.request.get(`/boards/${board.id}`); expect(final.status()).toBe(200);
      expect((await final.json()).board).toMatchObject({ name: 'Retained draft', description: 'Reviewed draft description', version: board.version + 3, backgroundType: 'COLOR', backgroundValue: 'purple' });
      await page.reload(); await expect(page.getByRole('heading', { name: 'Retained draft', exact: true })).toBeVisible();
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveCSS('background-color', 'rgb(250, 245, 255)');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await other.close();
    } finally { restoreWorker(); }
  });
}
