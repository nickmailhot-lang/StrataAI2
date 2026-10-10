import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) for (const action of ['read', 'edit'] as const) {
  test(`PRD-10 / NFR-FR-010: label management ${action} retains actual failure reference at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    await registerNotificationAccount(context.request, { email: `label-management-reference-${action}-${width}-${Date.now()}@example.test`,
      password: 'label-management-reference-correct-horse', displayName: 'Label management owner' });
    const orgResponse = await context.request.post('/organizations', { headers, data: { name: 'Label management reference Organization' } });
    expect(orgResponse.status()).toBe(201); const org = (await orgResponse.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Label management reference Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const root = `/boards/${board}/labels`, path = `/app/${org}/boards/${board}`;
    const created = await context.request.post(root, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Original label', color: 'green' } });
    expect(created.status()).toBe(201); const label = (await created.json()).id;
    const firstPage = await context.request.get(root); expect(firstPage.status()).toBe(200); let canonical = await firstPage.json();
    const restore = scopedBoardWorker(org); const expectedReference = (action === 'read' ? 'r' : 'e').repeat(64);
    let reference: string | undefined, dispatches = 0;
    try {
      await waitForBoardDelivery(context.request, board);
      if (action === 'read') await page.route(url => url.pathname === root, async route => {
        if (route.request().method() !== 'GET' || dispatches) { await route.continue(); return; }
        dispatches++;
        const response = await route.fetch({ url: route.request().url() + '?after=not-a-guid',
          headers: { ...route.request().headers(), 'X-Correlation-ID': expectedReference } });
        expect(response.status()).toBe(400); reference = response.headers()['x-correlation-id'];
        expect(reference).toBe(expectedReference); await route.fulfill({ response });
      });
      else await page.route(url => url.pathname === `/labels/${label}`, async route => {
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        dispatches++; expect(dispatches).toBe(1);
        expect(route.request().postDataJSON()).toEqual({ name: 'Reviewed draft', color: 'green', version: 1 });
        // A real admitted competing mutation commits after this UI submission.
        // The original version-1 command must be refused, never overwrite it.
        const competing = await context.request.patch(`/labels/${label}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
          data: { name: 'Concurrent canonical label', color: 'blue', version: 1 } });
        expect(competing.status()).toBe(200);
        const page = await context.request.get(root); expect(page.status()).toBe(200); canonical = await page.json();
        const response = await route.fetch({ headers: { ...route.request().headers(), 'X-Correlation-ID': expectedReference } });
        expect(response.status()).toBe(409); reference = response.headers()['x-correlation-id'];
        expect(reference).toBe(expectedReference); await route.fulfill({ response });
      });
      const reads = trackBoardReads(page, board, path); await page.goto(path);
      await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      await pressAdmittedAction(page.getByRole('button', { name: 'Manage labels', exact: true }));
      const dialog = page.getByRole('dialog', { name: 'Manage Board labels', exact: true });
      if (action === 'edit') {
        await pressAdmittedAction(dialog.getByRole('button', { name: 'Edit Original label (green)', exact: true }));
        await dialog.getByLabel('Label name (optional)', { exact: true }).fill('Reviewed draft');
        await pressAdmittedAction(dialog.getByRole('button', { name: 'Save label', exact: true }));
      }
      await expect(dialog.getByText(action === 'read' ? 'Unable to load current labels. Reload labels or refresh the Board.'
        : 'This item changed elsewhere. Refresh the board before trying again.', { exact: true })).toBeVisible();
      expect(dispatches).toBe(1); expect(reference).toBeDefined();
      const unchanged = await context.request.get(root); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(canonical);
      await expect(dialog.getByText(`Reference: ${reference}`, { exact: true })).toBeVisible();
      expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Reload labels', exact: true }));
      await expect(dialog.getByRole('button', { name: action === 'read' ? 'Edit Original label (green)' : 'Edit Concurrent canonical label (blue)', exact: true })).toBeEnabled();
      await expect(dialog.getByText(/^Reference:/)).toHaveCount(0);
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Done', exact: true })); await expect(dialog).toHaveCount(0);
      expect(dispatches).toBe(1);
      const final = await context.request.get(root); expect(final.status()).toBe(200); expect(await final.json()).toEqual(canonical);
    } finally { restore(); }
  });
}
