import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) {
  test(`PRD-10 / NFR-FR-010: label creation retains actual safe server reference at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    await registerNotificationAccount(context.request, { email: `label-reference-${width}-${Date.now()}@example.test`,
      password: 'label-reference-correct-horse', displayName: 'Label reference owner' });
    const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'Label reference Organization' } });
    expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Label reference Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const root = `/boards/${board}/labels`, path = `/app/${org}/boards/${board}`;
    const before = await context.request.get(root); expect(before.status()).toBe(200); expect((await before.json()).items).toHaveLength(0);
    const restore = scopedBoardWorker(org); const writes: { key: string; body: string }[] = []; let reference: string | undefined;
    try {
      await waitForBoardDelivery(context.request, board);
      await page.route(url => url.pathname === root, async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
        expect(JSON.parse(writes.at(-1)!.body)).toEqual({ name: 'Release ready', color: 'green' });
        // Exercise actual server input refusal, rather than fabricating an error
        // body/reference. The following valid command still uses its real UI body.
        const response = writes.length === 1 ? await route.fetch({
          postData: JSON.stringify({ name: 'Release ready', color: 'invalid-color' }),
          headers: { ...route.request().headers(), 'X-Correlation-ID': 'r'.repeat(64) },
        }) : await route.fetch();
        expect(response.status()).toBe(writes.length === 1 ? 400 : 201);
        if (writes.length === 1) {
          reference = response.headers()['x-correlation-id'];
          expect(reference).toBe('r'.repeat(64));
        }
        await route.fulfill({ response });
      });
      const reads = trackBoardReads(page, board, path); await page.goto(path);
      await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      await pressAdmittedAction(page.getByRole('button', { name: 'Create label', exact: true }));
      await page.getByLabel('Label name (optional)', { exact: true }).fill('Release ready');
      const dialog = page.getByRole('dialog', { name: 'Create Board label', exact: true });
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Create', exact: true }));
      await expect(dialog.getByText('Check the fields and try again.', { exact: true })).toBeVisible();
      expect(reference).toBeDefined(); await expect(dialog.getByText(`Reference: ${reference}`, { exact: true })).toBeVisible();
      await expect(dialog.getByLabel('Label name (optional)', { exact: true })).toHaveValue('Release ready');
      expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      const rejected = await context.request.get(root); expect(rejected.status()).toBe(200); expect(await rejected.json()).toEqual(await before.json());
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Create', exact: true }));
      await expect(dialog).toHaveCount(0); await expect(page.getByText(/^Reference:/)).toHaveCount(0);
      expect(writes).toHaveLength(2); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(writes[1].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(writes[1].key).not.toBe(writes[0].key); expect(writes[1].body).toBe(writes[0].body);
      const final = await context.request.get(root); expect(final.status()).toBe(200);
      const labels = (await final.json()).items; expect(labels).toHaveLength(1);
      expect(labels[0]).toMatchObject({ name: 'Release ready', color: 'green', version: 1, deleted: false });
    } finally { restore(); }
  });
}
