import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { pressObservedLocalAction } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-11-VS-01-T-02 / UX-CARD-02: actual zero-assignee Card previews stay authoritative at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 900 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `empty-preview-${width}-${Date.now()}@example.test`, password: 'preview-correct-horse-2026', displayName: 'Preview Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'Card preview council' } });
    expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Preview work', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const restoreWorker = scopedBoardWorker(org);
    try {
      const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'To do' } });
      expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
      const emptyReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Unassigned work', description: 'Ready to choose an assignee.' } });
      const assignedReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Assigned work' } });
      expect(emptyReply.status()).toBe(201); expect(assignedReply.status()).toBe(201);
      const empty = (await emptyReply.json()).id, assigned = (await assignedReply.json()).id;
      const assignment = await context.request.put(`/cards/${assigned}/members/${actor}?version=1`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } });
      expect(assignment.status()).toBe(200);
      await waitForBoardDelivery(context.request, board);
      const detailed = await context.request.get(`/cards/${empty}/members`); expect(detailed.status()).toBe(200);
      expect(await detailed.json()).toMatchObject({ organizationId: org, boardId: board, cardId: empty, cardVersion: 1, items: [], nextCursor: null });
      let stale = false, browserWrites = 0;
      await page.route(url => url.pathname === `/boards/${board}`, async route => {
        if (route.request().method() !== 'GET') return route.continue();
        const response = await route.fetch();
        if (response.status() !== 200 || !stale) return route.fulfill({ response });
        const body = await response.json(); body.cardMembers[empty].cardVersion++;
        return route.fulfill({ response, json: body });
      });
      page.on('request', request => {
        if (!['GET', 'HEAD'].includes(request.method()) && /^\/(organizations|boards|cards|lists)(\/|$)/.test(new URL(request.url()).pathname)) browserWrites++;
      });
      await page.goto(`/app/${org}/boards/${board}`);
      const emptyFace = page.getByRole('link', { name: 'Unassigned work', exact: true });
      const assignedFace = page.getByRole('link', { name: 'Assigned work', exact: true });
      await expect(emptyFace.getByText('Unassigned', { exact: true })).toBeVisible();
      await expect(assignedFace.getByRole('img', { name: 'Assigned to Preview Owner', exact: true })).toBeVisible();
      const canonical = await context.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200);
      const snapshot = await canonical.json();
      expect(snapshot.cardMembers[empty]).toEqual({ items: [], total: 0, cardVersion: 1 });
      expect(snapshot.cardMembers[assigned]).toMatchObject({ total: 1, cardVersion: 2, items: [{ userId: actor, displayName: 'Preview Owner' }] });
      await pressObservedLocalAction(emptyFace);
      const dialog = page.getByRole('dialog', { name: 'Card details', exact: true }); await expect(dialog).toBeVisible();
      await pressObservedLocalAction(dialog.getByRole('button', { name: 'Show assignees', exact: true }));
      await expect(dialog.getByText('No assignees on this page.', { exact: true })).toBeVisible();
      await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await pressObservedLocalAction(dialog.getByRole('button', { name: 'Close', exact: true }));
      await expect(dialog).toHaveCount(0); await expect(emptyFace).toBeFocused();
      stale = true; await pressObservedLocalAction(page.getByRole('button', { name: 'Refresh board', exact: true }));
      await expect(emptyFace.getByText('Assignees unavailable', { exact: true })).toBeVisible();
      await expect(emptyFace.getByText('Unassigned', { exact: true })).toHaveCount(0);
      stale = false; await pressObservedLocalAction(page.getByRole('button', { name: 'Refresh board', exact: true }));
      await expect(emptyFace.getByText('Unassigned', { exact: true })).toBeVisible();
      expect(browserWrites).toBe(0);
      expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      await expect(page).toHaveURL(/\/login$/, { timeout: 20_000 });
      await expect(emptyFace).toHaveCount(0); await expect(assignedFace).toHaveCount(0);
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(404);
    } finally { restoreWorker(); }
  });
}
