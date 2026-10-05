import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-16 idle Board filter account withdrawal on a still-viewable PUBLIC Board at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(60_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }, suffix = `${Date.now()}-${width}`;
    const owner = { email: `filter-owner-${suffix}@example.test`, password: 'filter-owner-correct-horse', displayName: 'Filter owner fixture' };
    expect((await context.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Filter account fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Public filter fixture', visibility: 'PUBLIC' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    expect((await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Personal roof match' } })).status()).toBe(201);
    const origin = new URL(boardReply.url()).origin;
    const observer = await browser.newContext({ baseURL: origin, storageState: await context.storageState() });
    const incoming = await browser.newContext({ baseURL: origin });
    let restoreWorker: (() => void) | undefined;
    try {
      const visitor = { email: `filter-visitor-${suffix}@example.test`, password: 'filter-visitor-correct-horse', displayName: 'Incoming filter fixture' };
      expect((await incoming.request.post('/auth/register', { headers, data: visitor })).status()).toBe(201);
      expect((await incoming.request.post('/auth/login', { headers, data: visitor })).status()).toBe(200);
      const visitorActor = (await (await incoming.request.get('/me')).json()).id;
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const ownerActor = (await (await observer.request.get('/me')).json()).id;
      const beforeReply = await observer.request.get(`/boards/${board}`); expect(beforeReply.status()).toBe(200); const before = await beforeReply.json();
      let changes = 0;
      page.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname === `/boards/${board}/cards/filter-change`) changes++; });
      await page.goto(`/app/${org}/boards/${board}`);
      const trigger = page.getByRole('button', { name: 'Filter Board Cards', exact: true }); await expect(trigger).toBeEnabled(); await trigger.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Filter Board Cards', exact: true });
      const keyword = dialog.getByRole('textbox', { name: 'Card keyword', exact: true }); await expect(keyword).toBeEnabled(); await keyword.fill('Personal roof');
      const apply = dialog.getByRole('button', { name: 'Apply filters', exact: true }); await expect(apply).toBeEnabled();
      const originalSourceReply = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === `/boards/${board}/cards/filter-change`);
      await apply.press('Enter');
      const originalReply = await originalSourceReply; expect(originalReply.status()).toBe(200); const originalSource = await originalReply.json();
      expect(originalSource).toMatchObject({ actorId: ownerActor, organizationId: org, boardId: board, eventType: 'BOARD_FILTER_CHANGED' });
      await expect(dialog.getByRole('link', { name: 'Personal roof match — Planning', exact: true })).toBeVisible(); expect(changes).toBe(1);
      // Replace the actual browser cookie, retaining the old session in the
      // independent observer. The PUBLIC Board remains viewable, so loss of
      // Board access cannot substitute for checking the personal filter actor.
      expect((await context.request.post('/auth/login', { headers, data: visitor })).status()).toBe(200);
      expect((await (await context.request.get('/me')).json()).id).toBe(visitorActor);
      expect((await context.request.get(`/boards/${board}/cards`)).status()).toBe(200);
      await expect(dialog).toHaveCount(0, { timeout: 20_000 });
      expect(changes).toBe(1);
      const retainedReply = await observer.request.get(`/boards/${board}`); expect(retainedReply.status()).toBe(200); expect(await retainedReply.json()).toEqual(before);
      await expect(trigger).toBeEnabled(); await trigger.press('Enter'); await expect(keyword).toBeEnabled(); await expect(keyword).toHaveValue('');
      await expect(dialog.getByRole('button', { name: 'Retry original filter change', exact: true })).toHaveCount(0); expect(changes).toBe(1);
      // Recovery must create an incoming-actor observation, without borrowing
      // the previous account's acknowledgment or writing shared Board state.
      await keyword.fill('Personal roof'); await expect(apply).toBeEnabled();
      const recoveredSourceReply = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === `/boards/${board}/cards/filter-change`);
      await apply.press('Enter'); const recoveredReply = await recoveredSourceReply;
      expect(recoveredReply.status()).toBe(200); expect(recoveredReply.headers()['cache-control']).toContain('no-store');
      expect(recoveredReply.request().headers()['x-strataai-expected-actor']).toBe(visitorActor);
      expect(recoveredReply.request().headers()['idempotency-key']).not.toBe(originalReply.request().headers()['idempotency-key']);
      expect(recoveredReply.request().postData()).toBeNull();
      const recoveredSource = await recoveredReply.json();
      expect(Object.keys(recoveredSource).sort()).toEqual(['actorId','boardId','createdAt','entityId','entityType','eventId','eventType','metadata','organizationId','version']);
      expect(recoveredSource).toMatchObject({ actorId: visitorActor, organizationId: org, boardId: board, eventType: 'BOARD_FILTER_CHANGED', entityType: 'BoardFilter', version: 1, metadata: {} });
      expect(recoveredSource.entityId).toBe(recoveredSource.eventId); expect(recoveredSource.eventId).not.toBe(originalSource.eventId);
      expect(recoveredSource.eventId).toMatch(/^[0-9a-f-]{36}$/); expect(Number.isFinite(Date.parse(recoveredSource.createdAt))).toBe(true);
      await expect(dialog.getByRole('link', { name: 'Personal roof match — Planning', exact: true })).toBeVisible(); expect(changes).toBe(2);
      await dialog.getByRole('button', { name: 'Show this page on Board', exact: true }).press('Enter');
      await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
      await page.reload(); await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Personal roof match', exact: true })).toBeVisible(); expect(changes).toBe(2);
      const recoveredBoard = await observer.request.get(`/boards/${board}`); expect(recoveredBoard.status()).toBe(200); expect(await recoveredBoard.json()).toEqual(before);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { await observer.close(); await incoming.close(); restoreWorker?.(); }
  });
}
