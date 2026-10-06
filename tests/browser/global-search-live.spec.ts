import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-16 global search crosses Organizations and recovers changed and missed state at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `global-search-${width}-${Date.now()}@example.test`,
      password: 'global-search-correct-horse', displayName: 'Global search reader' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const records: { org: string; board: string; card: string; title: string; version: number }[] = [];
    for (const name of ['One', 'Two']) {
      const orgReply = await context.request.post('/organizations', { headers, data: { name: `Global search ${name}` } });
      expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
      const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: `Board ${name}`, visibility: 'PRIVATE' } });
      expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
      const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
      expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
      const title = `Global needle ${name}`;
      const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title } });
      expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
      const labelReply = await context.request.post(`/boards/${board}/labels`, { headers, data: { name: 'Priority', color: 'red' } });
      expect(labelReply.status()).toBe(201); const label = (await labelReply.json()).id;
      expect((await context.request.put(`/cards/${card}/labels/${label}?version=1`, { headers, data: {} })).status()).toBe(200);
      expect((await context.request.put(`/cards/${card}/members/${actor}?version=2`, { headers, data: {} })).status()).toBe(200);
      records.push({ org, board, card, title, version: 3 });
    }
    let restoreWorker = () => {};
    let closePeer = async () => {};
    try {
      await page.goto(`/app/${records[0].org}/search`);
      await page.getByRole('textbox', { name: 'Card text', exact: true }).fill('needle');
      await page.getByRole('textbox', { name: 'Label name', exact: true }).fill('prior');
      await page.getByRole('textbox', { name: 'Member name', exact: true }).fill('search reader');
      const searchReply = page.waitForResponse(response => response.request().method() === 'GET'
        && new URL(response.url()).pathname === '/search' && response.status() === 200);
      await page.getByRole('button', { name: 'Search', exact: true }).press('Enter');
      const searchPage = await (await searchReply).json(); const source = searchPage.interaction;
      expect(Object.keys(source).sort()).toEqual(['actorId', 'boardId', 'createdAt', 'entityId', 'entityType', 'eventId', 'eventType', 'metadata', 'organizationId', 'version']);
      expect(source.eventType).toBe('SEARCH_EXECUTED'); expect(source.entityType).toBe('Search');
      expect(source.actorId).toBe(actor); expect(source.organizationId).toBeNull(); expect(source.boardId).toBeNull();
      expect(source.eventId).toMatch(/^[0-9a-f-]{36}$/); expect(source.entityId).toBe(source.eventId);
      expect(source.version).toBe(1); expect(source.metadata).toEqual({}); expect(Number.isFinite(Date.parse(source.createdAt))).toBe(true);
      await expect(page.getByText('Search acknowledged.', { exact: true })).toBeVisible();
      const firstLink = page.getByRole('link', { name: /^Global needle/ });
      await expect(firstLink).toHaveCount(1);
      const firstTitle = await firstLink.innerText(); const current = records.find(r => r.title === firstTitle)!;
      expect(current).toBeDefined();
      await expect(firstLink).toHaveAttribute('href', `/app/${current.org}/boards/${current.board}/cards/${current.card}`);
      await expect(page.getByText('Labels: Priority', { exact: true })).toBeVisible();
      await expect(page.getByText('Members: Global search reader', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
      await expect(page.getByRole('link', { name: records.find(r => r !== current)!.title, exact: true })).toBeVisible();
      // Start again to observe the first canonical result on its initial page.
      await page.getByRole('button', { name: 'Search', exact: true }).press('Enter');
      await expect(page.getByRole('link', { name: current.title, exact: true })).toBeVisible();
      restoreWorker = scopedBoardWorker(current.org); await waitForBoardDelivery(context.request, current.board);
      const peer = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
      closePeer = () => peer.close();
      expect((await peer.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const editor = await peer.newPage(); const cardPath = `/app/${current.org}/boards/${current.board}/cards/${current.card}`;
      const reads = trackBoardReads(editor, current.board, cardPath);
      const revision = trackCardVersion(editor, current.board, current.card, cardPath);
      await editor.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await expect.poll(revision).toBe(current.version);
      async function rename(title: string) {
        const field = editor.getByRole('textbox', { name: 'Card title', exact: true });
        await expect(field).toBeEnabled(); await field.fill(title);
        const save = editor.getByRole('button', { name: 'Save card', exact: true }); await expect(save).toBeEnabled();
        const acknowledgment = editor.waitForResponse(response => response.request().method() === 'PATCH'
          && new URL(response.url()).pathname === `/cards/${current.card}`);
        await save.press('Enter'); const reply = await acknowledgment; expect(reply.status()).toBe(200);
        const value = await reply.json(); expect(value.version).toBe(current.version + 1); current.version = value.version;
        await waitForBoardDelivery(peer.request, current.board); await expect.poll(revision).toBe(current.version);
        await expect(field).toBeEnabled(); await expect(field).toHaveValue(title);
      }
      await rename('Global needle changed');
      await expect(page.getByRole('link', { name: 'Global needle changed', exact: true })).toBeVisible({ timeout: 25_000 });
      await context.setOffline(true);
      await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
      await expect(page.getByText(/Search is unavailable/)).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('link', { name: 'Global needle changed', exact: true })).toHaveCount(0);
      await rename('Global needle recovered');
      await context.setOffline(false);
      await expect(page.getByRole('link', { name: 'Global needle recovered', exact: true })).toBeVisible({ timeout: 30_000 });
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      // A real session withdrawal must purge current result disclosure and
      // submitted criteria through foreground recovery, without a page reload.
      // The independent editor session remains admitted and proves that the
      // withdrawal does not mutate the protected Board graph.
      const beforeWithdrawal = await peer.request.get(`/boards/${current.board}`);
      expect(beforeWithdrawal.status()).toBe(200); const protectedBefore = await beforeWithdrawal.json();
      expect((await context.request.post('/auth/logout', {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {},
      })).status()).toBe(204);
      await expect(page.getByText('Access to this Organization surface is unavailable.', { exact: true })).toBeVisible({ timeout: 25_000 });
      await expect(page.getByRole('link', { name: /^Global needle/ })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Next search page', exact: true })).toHaveCount(0);
      for (const name of ['Card text', 'Label name', 'Member name'])
        await expect(page.getByRole('textbox', { name, exact: true })).toHaveCount(0);
      expect((await context.request.get('/search?q=needle')).status()).toBe(401);
      const afterWithdrawal = await peer.request.get(`/boards/${current.board}`);
      expect(afterWithdrawal.status()).toBe(200); expect(await afterWithdrawal.json()).toEqual(protectedBefore);
      expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      await page.getByRole('button', { name: 'Check access again', exact: true }).press('Enter');
      for (const name of ['Card text', 'Label name', 'Member name'])
        await expect(page.getByRole('textbox', { name, exact: true })).toHaveValue('');
      await page.getByRole('textbox', { name: 'Card text', exact: true }).fill('needle recovered');
      await page.getByRole('button', { name: 'Search', exact: true }).press('Enter');
      await expect(page.getByRole('link', { name: 'Global needle recovered', exact: true })).toBeVisible();
    } finally { try { await context.setOffline(false); } finally { try { restoreWorker(); } finally { await closePeer(); } } }
  });
}

for (const width of [1280, 390]) {
  test(`PRD-02 search deadlines follow current account preferences at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `search-timezone-${width}-${Date.now()}@example.test`, password: 'search-timezone-correct-horse',
      displayName: 'Search timezone reader', locale: 'en-US', timezone: 'Pacific/Honolulu' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Search deadline council' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Deadline Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Deadlines' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Timezone deadline fixture' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    expect((await context.request.patch(`/cards/${card}/dates`, { headers, data: {
      startAt: null, dueAt: '2040-01-02T00:30:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: false, version: 1,
    } })).status()).toBe(200);
    await page.goto(`/app/${org}/search`);
    await page.getByRole('textbox', { name: 'Card text', exact: true }).fill('Timezone deadline fixture');
    const search = page.getByRole('button', { name: 'Search', exact: true });
    await search.focus(); await expect(search).toBeFocused(); await search.press('Enter');
    await expect(page.getByText(/Due Jan 1, 2040, 14:30/)).toBeVisible();
    const profile = await (await context.request.get('/me')).json();
    expect((await context.request.patch('/me', { headers, data: { timezone: 'Asia/Tokyo', version: profile.version } })).status()).toBe(200);
    await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
    await expect(page.getByText(/Due Jan 2, 2040, 09:30/)).toBeVisible();
    await expect(page.getByText(/Due Jan 1/)).toHaveCount(0);
    const boardRead = await context.request.get(`/boards/${board}`);
    expect(boardRead.status()).toBe(200);
    let boardVersion = (await boardRead.json()).board.version;
    for (const [timezone, deadline] of [
      ['Pacific/Honolulu', /Due Jan 1, 2040, 14:30/],
      ['UTC', /Due Jan 2, 2040, 00:30/],
      [null, /Due Jan 2, 2040, 09:30/],
    ] as const) {
      const policy = await context.request.patch(`/boards/${board}/date-policy`, { headers, data: { timezone, version: boardVersion } });
      expect(policy.status()).toBe(200); boardVersion = (await policy.json()).board.version;
      await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
      await expect(page.getByText(deadline)).toBeVisible();
      const persisted = await context.request.get(`/boards/${board}`);
      expect(persisted.status()).toBe(200);
      const snapshot = await persisted.json();
      const persistedCard = snapshot.lists.flatMap((entry: { cards: { id: string; dueAt: string }[] }) => entry.cards)
        .find((entry: { id: string }) => entry.id === card);
      expect(persistedCard).toBeDefined();
      expect(new Date(persistedCard.dueAt).toISOString()).toBe('2040-01-02T00:30:00.000Z');
    }
    const dateOnlyPolicy = await context.request.patch(`/boards/${board}/date-policy`, {
      headers, data: { timezone: 'Pacific/Honolulu', version: boardVersion },
    });
    expect(dateOnlyPolicy.status()).toBe(200);
    expect((await context.request.patch(`/cards/${card}/dates`, { headers, data: {
      startAt: null, dueAt: '2040-01-02', dueTimezone: 'Pacific/Honolulu', dueHasTime: false, dueComplete: false, version: 2,
    } })).status()).toBe(200);
    await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
    await expect(page.getByText('Due Jan 2, 2040', { exact: true })).toBeVisible();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
