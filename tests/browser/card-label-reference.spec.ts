import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) for (const action of ['disclosure', 'options', 'assignment'] as const) {
  test(`PRD-10 / NFR-FR-010: Card label ${action} retains actual refusal reference at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    await registerNotificationAccount(context.request, { email: `card-label-reference-${action}-${width}-${Date.now()}@example.test`,
      password: 'card-label-reference-correct-horse', displayName: 'Card label owner' });
    const orgResponse = await context.request.post('/organizations', { headers, data: { name: 'Card label reference Organization' } });
    expect(orgResponse.status()).toBe(201); const org = (await orgResponse.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Card label reference Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Reference List' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Reference Card' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const labelResponse = await context.request.post(`/boards/${board}/labels`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { name: 'Reference label', color: 'green' } });
    expect(labelResponse.status()).toBe(201); const label = (await labelResponse.json()).id;
    const root = `/cards/${card}/labels`, options = `/cards/${card}/label-options`, path = `/app/${org}/boards/${board}/cards/${card}`;
    const first = await context.request.get(root); expect(first.status()).toBe(200); const canonical = await first.json();
    const boardBefore = await context.request.get(`/boards/${board}`); expect(boardBefore.status()).toBe(200); const originalBoard = await boardBefore.json();
    const referenceRequest = 'c'.repeat(64); let reference: string | undefined;
    const dispatches: { method: string; key: string | undefined }[] = [];
    const restore = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const intercepted = action === 'disclosure' ? root : action === 'options' ? options : `${root}/${label}`;
      await page.route(url => url.pathname === intercepted, async route => {
        if (dispatches.length) { await route.continue(); return; }
        const request = route.request(); const method = action === 'assignment' ? 'PUT' : 'GET'; expect(request.method()).toBe(method);
        dispatches.push({ method, key: request.headers()['idempotency-key'] });
        const url = new URL(request.url());
        if (action === 'assignment') { expect(url.searchParams.get('version')).toBe('1'); url.searchParams.set('version', 'invalid'); }
        else url.searchParams.set('after', 'not-a-guid');
        const response = await route.fetch({ url: url.href, headers: { ...request.headers(), 'X-Correlation-ID': referenceRequest } });
        expect(response.status()).toBe(400); reference = response.headers()['x-correlation-id']; expect(reference).toBe(referenceRequest);
        await route.fulfill({ response });
      });
      const reads = trackBoardReads(page, board, path); await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      // The Card dialog hides the underlying Board from the accessibility tree.
      // Its real admission state must still be idle before invoking Card controls.
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
      await pressAdmittedAction(dialog.getByRole('button', { name: action === 'disclosure' ? 'Show labels' : 'Edit Card labels', exact: true }));
      if (action === 'assignment') await pressAdmittedAction(dialog.getByRole('button', { name: 'Add label Reference label', exact: true }));
      await expect(dialog.getByText(action === 'disclosure' ? 'Unable to load current labels. Refresh the Board or try again.'
        : 'Labels changed or could not be loaded. Refresh the Board, then reload label options.', { exact: true })).toBeVisible();
      expect(dispatches).toHaveLength(1); expect(reference).toBeDefined();
      const unchanged = await context.request.get(root); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(canonical);
      const unchangedBoard = await context.request.get(`/boards/${board}`); expect(unchangedBoard.status()).toBe(200); expect(await unchangedBoard.json()).toEqual(originalBoard);
      await expect(dialog.getByText(`Reference: ${reference}`, { exact: true })).toBeVisible();
      expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await pressAdmittedAction(dialog.getByRole('button', { name: action === 'disclosure' ? 'Retry labels' : 'Reload label options', exact: true }));
      if (action === 'disclosure') await expect(dialog.getByText('No labels assigned.', { exact: true })).toBeVisible();
      else await expect(dialog.getByRole('button', { name: 'Add label Reference label', exact: true })).toBeEnabled();
      await expect(dialog.getByText(/^Reference:/)).toHaveCount(0); expect(dispatches).toHaveLength(1);
      const final = await context.request.get(root); expect(final.status()).toBe(200); expect(await final.json()).toEqual(canonical);
      const finalBoard = await context.request.get(`/boards/${board}`); expect(finalBoard.status()).toBe(200); expect(await finalBoard.json()).toEqual(originalBoard);
    } finally { restore(); }
  });
}
