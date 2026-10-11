import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

const headers = { 'X-StrataAI-Request': '1' };
async function press(page: Page, name: string) {
  const button = page.getByRole('button', { name, exact: true });
  await expect(button).toBeEnabled(); await button.focus(); await page.keyboard.press('Enter');
}
async function sourceEntry(page: Page, kind: string) {
  if (kind === 'cardComment') await press(page, 'Review Card comments');
  if (kind === 'attachment') await press(page, 'Show attachments');
  await press(page, `Retention classification for this ${kind === 'card' ? 'Card' : kind === 'cardComment' ? 'Comment' : 'Attachment'}`);
}

for (const width of [1280, 390]) {
  test(`PRD-70 membership withdrawal clears pending source recovery without replay at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {};
    try {
      const verified = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
      await registerNotificationAccount(owner.request, {
        email: `source-withdraw-owner-${width}-${crypto.randomUUID()}@example.test`,
        password: 'source-withdraw-owner-fixture', displayName: 'Source withdrawal Owner',
      }, verified);
      const email = `source-withdraw-admin-${width}-${crypto.randomUUID()}@example.test`;
      const admin = await registerNotificationAccount(context.request, { email,
        password: 'source-withdraw-admin-fixture', displayName: 'Source withdrawal administrator' }, verified);
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Source withdrawal fixture' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const invitation = await owner.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private withdrawal Board', visibility: 'PRIVATE' } });
      expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
      const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Private source List' } });
      expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
      const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Private source before withdrawal' } });
      expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
      const path = `/organizations/${org}/retention-sources/card/${card}/classification`;
      const writes: { body: string; key: string }[] = [];
      await page.route(url => url.pathname === path, async route => {
        const request = route.request(), response = await route.fetch(); expect(response.status()).toBe(200);
        writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'] });
        await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"work_storage_unavailable"}' });
      });
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`, reads = trackBoardReads(page, board, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await sourceEntry(page, 'card'); await press(page, 'Review source classification');
      await expect(page.getByRole('textbox', { name: 'Record category', exact: false })).toBeFocused();
      await page.keyboard.insertText('private.withdrawal');
      await page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true }).focus();
      await page.keyboard.press('Space'); await press(page, 'Confirm classification');
      await expect(page.getByRole('button', { name: 'Retry original classification', exact: true })).toBeFocused();
      expect(writes).toHaveLength(1);
      const storageKey = `strataai:retention-source-classification:v1:${admin.user.id}:${org}`;
      expect(await page.evaluate(key => sessionStorage.getItem(key) !== null, storageKey)).toBe(true);
      const memberResponse = await owner.request.get(`/organizations/${org}/members/${admin.user.id}`);
      expect(memberResponse.status()).toBe(200); const member = (await memberResponse.json()).member;
      expect((await owner.request.delete(`/organizations/${org}/members/${admin.user.id}?expectedVersion=${member.version}`,
        { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } })).status()).toBe(204);
      // Board deep links retain their own safe denial view after the outer
      // Organization surface withdraws; require that authoritative Board state.
      await expect(page.getByRole('alert').filter({ hasText: 'This board or action is unavailable.' })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('button', { name: 'Retry original classification', exact: true })).toHaveCount(0);
      await expect.poll(() => page.evaluate(key => sessionStorage.getItem(key), storageKey)).toBeNull();
      expect(await page.locator('body').innerText()).not.toContain('Private source before withdrawal');
      expect(await page.locator('body').innerText()).not.toContain('private.withdrawal');
      expect(writes).toHaveLength(1);
      const refused = await context.request.post(path, { headers: { ...headers, 'Idempotency-Key': writes[0].key }, data: JSON.parse(writes[0].body) });
      expect(refused.status()).toBe(404); expect(await refused.text()).not.toContain('private.withdrawal');
      const preserved = await owner.request.get(`/boards/${board}`); expect(preserved.status()).toBe(200);
      expect((await preserved.json()).lists.flatMap((item: { cards: { id: string; version: number }[] }) => item.cards)
        .find((item: { id: string }) => item.id === card).version).toBe(1);
      const replacement = await owner.request.post(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: JSON.parse(writes[0].body) });
      // A fresh actor/key cannot disclose or replace an existing classification.
      expect(replacement.status()).toBe(404);
      expect((await replacement.json()).code).toBe('retention_source_unavailable');
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    } finally { restoreWorker(); await owner.close(); }
  });
  test(`PRD-70 original classification recovery retains its source Board after a real Card move at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    await registerNotificationAccount(context.request, {
      email: `source-move-${width}-${crypto.randomUUID()}@example.test`,
      password: 'source-move-fixture-correct-horse', displayName: 'Source move Owner',
    }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Classification move fixture' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boards: string[] = [], lists: string[] = [];
    for (const name of ['Original classification Board', 'Current source Board']) {
      const board = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(board.status()).toBe(201); boards.push((await board.json()).id);
      const list = await context.request.post(`/boards/${boards.at(-1)}/lists`, { headers, data: { name: 'Source context' } });
      expect(list.status()).toBe(201); lists.push((await list.json()).id);
    }
    const cardResponse = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Moving classified source' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const path = `/organizations/${org}/retention-sources/card/${card}/classification`;
    const writes: { body: string; key: string; receipt: unknown }[] = [];
    await page.route(url => url.pathname === path, async route => {
      const request = route.request(), response = await route.fetch(); expect(response.status()).toBe(200);
      writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'], receipt: await response.json() });
      if (writes.length === 1) await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"work_storage_unavailable"}' });
      else await route.fulfill({ response });
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      for (const board of boards) await waitForBoardDelivery(context.request, board);
      const originalPath = `/app/${org}/boards/${boards[0]}/cards/${card}`, originalReads = trackBoardReads(page, boards[0], originalPath);
      await page.goto(originalPath); await expect.poll(originalReads).toBeGreaterThanOrEqual(2);
      await sourceEntry(page, 'card'); await press(page, 'Review source classification');
      await expect(page.getByRole('textbox', { name: 'Record category', exact: false })).toBeFocused();
      await page.keyboard.insertText('reviewed.before.move');
      await page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true }).focus();
      await page.keyboard.press('Space'); await press(page, 'Confirm classification');
      await expect(page.getByRole('button', { name: 'Retry original classification', exact: true })).toBeFocused();
      const moved = await context.request.post(`/cards/${card}/move`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { sourceBoardId: boards[0], destinationListId: lists[1], expectedVersion: 1 } });
      expect(moved.status()).toBe(200);
      await waitForBoardDelivery(context.request, boards[1]);
      const currentPath = `/app/${org}/boards/${boards[1]}/cards/${card}`, currentReads = trackBoardReads(page, boards[1], currentPath);
      await page.goto(currentPath); await expect.poll(currentReads).toBeGreaterThanOrEqual(2);
      await sourceEntry(page, 'card');
      await expect(page.getByRole('button', { name: 'Review source classification', exact: true })).toHaveCount(0);
      await press(page, 'Retry original classification');
      await expect(page.getByText('Classification confirmed. The original source and audit evidence are preserved.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      expect(JSON.parse(writes[0].body).review).toMatchObject({ cardVersion: 1, sourceVersion: 1, boardId: boards[0], listId: lists[0], recordId: card });
      expect((writes[1].receipt as { reference: object }).reference).toMatchObject({ originalBoardId: boards[0], sourceVersion: 1, recordId: card });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { restoreWorker(); }
  });
  test(`PRD-70 actual three-source keyboard classification and exact original reload recovery at ${width}px`, async ({ page, context }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const account = await registerNotificationAccount(context.request, {
      email: `source-classification-${width}-${crypto.randomUUID()}@example.test`,
      password: 'source-classification-fixture-horse', displayName: 'Classification Owner',
    }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Source classification fixture' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Reviewed source Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Source List' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Actual retained source Card' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const commentResponse = await context.request.post(`/cards/${card}/comments`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { content: 'Actual source comment', cardVersion: 1 },
    });
    expect(commentResponse.status()).toBe(200);
    const comments = await context.request.get(`/cards/${card}/comments`); expect(comments.status()).toBe(200);
    const comment = (await comments.json()).items[0];
    const attachmentResponse = await context.request.post(`/cards/${card}/attachments/url`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { title: 'Actual source attachment', url: 'https://example.test/retained-source', cardVersion: 2 },
    });
    expect(attachmentResponse.status()).toBe(200);
    const attachments = await context.request.get(`/cards/${card}/attachments`); expect(attachments.status()).toBe(200);
    const attachment = (await attachments.json()).items[0];
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`;
      const reads = trackBoardReads(page, board, cardPath), events: string[] = [];
      for (const source of [{ kind: 'card', id: card, version: 3 },
        { kind: 'cardComment', id: comment.id, version: comment.version },
        { kind: 'attachment', id: attachment.id, version: attachment.version }]) {
        const path = `/organizations/${org}/retention-sources/${source.kind}/${source.id}/classification`;
        const matches = (url: URL) => url.pathname === path;
        const writes: { body: string; key: string; receipt: unknown }[] = [];
        await page.route(matches, async route => {
          const request = route.request(); expect(request.method()).toBe('POST');
          const response = await route.fetch(); expect(response.status()).toBe(200);
          writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'], receipt: await response.json() });
          if (writes.length === 1) await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"work_storage_unavailable"}' });
          else await route.fulfill({ response });
        });
        const before = reads(); await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(before + 2);
        await sourceEntry(page, source.kind); await press(page, 'Review source classification');
        const category = page.getByRole('textbox', { name: 'Record category', exact: false });
        await expect(category).toBeFocused(); await page.keyboard.insertText(`reviewed.${source.kind}`);
        await expect(page.getByRole('button', { name: 'Confirm classification', exact: true })).toBeDisabled();
        const confirm = page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true });
        await confirm.focus(); await page.keyboard.press('Space');
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await press(page, 'Confirm classification');
        const retry = page.getByRole('button', { name: 'Retry original classification', exact: true });
        await expect(retry).toBeFocused(); expect(writes).toHaveLength(1);
        const beforeReload = reads(); await page.reload(); await expect.poll(reads).toBeGreaterThanOrEqual(beforeReload + 2);
        await sourceEntry(page, source.kind); await press(page, 'Retry original classification');
        await expect(page.getByText('Classification confirmed. The original source and audit evidence are preserved.', { exact: true })).toBeVisible();
        expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
        expect(JSON.parse(writes[0].body)).toMatchObject({ confirmed: true, category: `reviewed.${source.kind}`, jurisdiction: null,
          review: { organizationId: org, kind: source.kind, recordId: source.id, cardId: card, boardId: board, cardVersion: 3, sourceVersion: source.version } });
        const receipt = writes[0].receipt as { actorId: string; eventId: string; reference: object };
        expect(receipt.actorId).toBe(account.user.id);
        expect(receipt.reference).toMatchObject({ organizationId: org, recordId: source.id, kind: source.kind, originalBoardId: board, sourceVersion: source.version });
        events.push(receipt.eventId);
        await page.unroute(matches);
      }
      expect(new Set(events).size).toBe(3);
      const snapshot = await context.request.get(`/boards/${board}`); expect(snapshot.status()).toBe(200);
      expect((await snapshot.json()).lists.flatMap((item: { cards: { id: string; version: number }[] }) => item.cards)
        .find((item: { id: string }) => item.id === card).version).toBe(3);
    } finally { restoreWorker(); }
  });
}
