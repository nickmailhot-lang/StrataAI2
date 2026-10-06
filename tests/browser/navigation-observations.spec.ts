import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 768, 390]) {
  test(`PRD-01 navigation originals, return recovery and keyboard Back at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `navigation-${width}-${Date.now()}@example.test`, password: 'navigation-correct-horse-battery', displayName: 'Navigation user' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Navigation browser Organization' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Navigation browser Board', visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201); const board = await created.json();
    const listReply = await context.request.post(`/boards/${board.id}/lists`, { headers, data: { name: 'Navigation browser List' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    const cardReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Navigation browser Card' } });
    expect(cardReply.status()).toBe(201); const card = await cardReply.json();
    const restoreWorker = scopedBoardWorker(org);
    try {
    await waitForBoardDelivery(context.request, board.id);
    const attempts: { key: string; event: unknown; query: string }[] = [];
    const observed = new Set<string>();
    await page.route('**/navigation/observations?*', async route => {
      const request = route.request(), query = new URL(request.url()).searchParams;
      expect(request.method()).toBe('POST'); expect(request.postData()).toBeNull();
      expect(request.headers()['x-strataai-expected-actor']).toBe(actor);
      const response = await route.fetch(); expect(response.status()).toBe(200);
      const event = await response.json();
      expect(Object.keys(event).sort()).toEqual(['actorId', 'boardId', 'createdAt', 'entityId', 'entityType', 'eventId', 'eventType', 'metadata', 'organizationId', 'version']);
      expect(event.actorId).toBe(actor); expect(event.metadata).toEqual({});
      observed.add(event.eventType);
      if (query.get('kind') === 'card') {
        expect(event.entityId).toBe(card.id); expect(event.boardId).toBe(board.id); expect(event.organizationId).toBe(org);
        attempts.push({ key: request.headers()['idempotency-key'], event, query: query.toString() });
        if (attempts.length === 1) {
          const edited = await context.request.patch(`/cards/${card.id}`, { headers,
            data: { title: card.title, description: 'Later navigation detail', version: card.version } });
          expect(edited.status()).toBe(200);
          expect((await edited.json()).version).toBe(card.version + 1);
          await route.abort('failed'); return;
        }
      }
      await route.fulfill({ response });
    });
    await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
    await expect.poll(() => observed.has('APPLICATION_CONTEXT_CHANGED')).toBe(true);
    const boardPath = `/app/${org}/boards/${board.id}`, cardPath = `${boardPath}/cards/${card.id}`;
    await page.goto(boardPath); await expect.poll(() => observed.has('BOARD_OPENED')).toBe(true);
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
    const link = page.getByRole('link', { name: 'Navigation browser Card', exact: true });
    await link.focus(); await link.press('Enter'); await expect(page).toHaveURL(new RegExp(`${cardPath}$`));
    await expect(page.getByRole('button', { name: 'Retry navigation confirmation', exact: true })).toBeVisible();
    expect(attempts).toHaveLength(1);
    await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
    await page.goto(cardPath);
    await expect.poll(() => attempts.length).toBe(2);
    expect(attempts[1]).toEqual(attempts[0]);
    await expect.poll(() => page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('strataai:navigation:v1:')).length)).toBe(0);
    await expect(page.getByRole('heading', { name: 'Card details', exact: true })).toBeVisible();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    const close = page.getByRole('button', { name: 'Close', exact: true }); await expect(close).toBeEnabled(); await close.focus(); await close.press('Enter');
    await expect(page).toHaveURL(new RegExp(`${boardPath}$`));
    await link.focus(); await link.press('Enter'); await expect(page).toHaveURL(new RegExp(`${cardPath}$`));
    await expect.poll(() => attempts.length).toBe(3);
    expect(attempts[2].key).not.toBe(attempts[0].key);
    await expect.poll(() => page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('strataai:navigation:v1:')).length)).toBe(0);
    await page.goBack(); await expect(page).toHaveURL(new RegExp(`${boardPath}$`));
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toBeVisible();
    } finally { restoreWorker(); }
  });
}
