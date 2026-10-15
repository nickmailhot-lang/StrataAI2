import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview, pressAdmittedAction } from './keyboardAdmission';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-13-TC-11/12: checklist editing preserves Board scroll and Card focus at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = await registerNotificationAccount(context.request, {
      email: `checklist-context-${width}-${Date.now()}@example.test`, password: 'checklist-context-correct-horse', displayName: 'Context reviewer',
    }, true);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Checklist context Organization' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist context Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    let first = '', card = '';
    for (let index = 0; index < 6; index++) {
      const reply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `Context column ${index + 1}` } });
      expect(reply.status()).toBe(201); if (!index) first = (await reply.json()).id;
    }
    for (let index = 0; index < 12; index++) {
      const reply = await context.request.post(`/lists/${first}/cards`, { headers, data: { title: `Context Card ${index + 1}` } });
      expect(reply.status()).toBe(201); if (index === 3) card = (await reply.json()).id;
    }
    const parent = await context.request.post(`/cards/${card}/checklists`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Context preparations', cardVersion: 1 },
    });
    expect(parent.status()).toBe(200); const checklist = (await parent.json()).checklist;
    const added = await context.request.post(`/cards/${card}/checklists/${checklist.id}/items`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Original context item', cardVersion: 2, checklistVersion: 1 },
    });
    expect(added.status()).toBe(200); const item = (await added.json()).item;
    const boardPath = `/app/${org}/boards/${board}`;
    const before = await context.request.get(`/boards/${board}`); expect(before.status()).toBe(200);
    const baseline = await before.json();
    let documents = 0, edits = 0, moves = 0;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      const path = new URL(request.url()).pathname;
      if (path === `/cards/${card}/checklists/${checklist.id}/items/${item.id}` && request.method() === 'PATCH') edits++;
      if (/\/cards\/[^/]+\/move$/.test(path) && request.method() === 'POST') moves++;
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const reads = trackBoardReads(page, board, boardPath);
      await page.goto(boardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      const canvas = page.locator('[aria-label="Kanban board"]');
      const column = page.getByRole('region', { name: 'Context column 1', exact: true });
      expect(await canvas.evaluate(node => node.scrollWidth > node.clientWidth)).toBe(true);
      expect(await column.evaluate(node => node.scrollHeight > node.clientHeight)).toBe(true);
      await canvas.evaluate(node => { node.scrollLeft = 20; });
      await column.evaluate(node => { node.scrollTop = 100; });
      const cardLink = page.getByRole('link', { name: 'Context Card 4', exact: true });
      await cardLink.evaluate(node => (node as HTMLElement).focus({ preventScroll: true }));
      await expect(cardLink).toBeFocused();
      const left = await canvas.evaluate(node => node.scrollLeft), top = await column.evaluate(node => node.scrollTop);
      expect(left).toBeGreaterThan(0); expect(top).toBeGreaterThan(0);
      await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Card details', exact: true }); await expect(dialog).toBeVisible();
      const manage = dialog.getByRole('button', { name: 'Manage checklists', exact: true });
      const items = dialog.getByRole('button', { name: 'Manage items in Context preparations', exact: true });
      const review = dialog.getByRole('button', { name: 'Review checklist items', exact: true });
      const edit = dialog.getByRole('button', { name: 'Edit item: Original context item', exact: true });
      const text = dialog.getByRole('textbox', { name: 'Checklist item text', exact: true });
      await openAdmittedReview(manage, items); await openAdmittedReview(items, review);
      await openAdmittedReview(review, edit); await openAdmittedReview(edit, text);
      await expect(text).toBeFocused(); await expect(text).toHaveValue('Original context item');
      await text.fill('Completed with context preserved');
      await pressAdmittedAction(dialog.getByRole('checkbox', { name: 'Item complete', exact: true }), 'Space');
      await expect(dialog.getByRole('checkbox', { name: 'Item complete', exact: true })).toBeChecked();
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Save checklist item', exact: true }));
      await expect(dialog.getByText('Checklist item saved.', { exact: true })).toBeVisible(); expect(edits).toBe(1);
      const current = await context.request.get(`/cards/${card}/checklists/${checklist.id}/items`);
      expect(current.status()).toBe(200); const canonical = await current.json();
      expect(canonical.cardVersion).toBe(4); expect(canonical.summary.checklist.version).toBe(3);
      expect(canonical.items).toHaveLength(1);
      expect(canonical.items[0]).toMatchObject({ id: item.id, text: 'Completed with context preserved', completed: true, completedBy: account.user.id, version: 2 });
      expect(canonical.items[0].completedAt).toBe(canonical.items[0].updatedAt);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Close', exact: true }));
      await expect(dialog).toHaveCount(0); await expect(page).toHaveURL(boardPath); await expect(cardLink).toBeFocused();
      expect(await canvas.evaluate(node => node.scrollLeft)).toBe(left);
      expect(await column.evaluate(node => node.scrollTop)).toBe(top);
      expect(edits).toBe(1); expect(moves).toBe(0); expect(documents).toBe(1);
      const final = await context.request.get(`/boards/${board}`); expect(final.status()).toBe(200);
      const expected = structuredClone(baseline.lists);
      for (const entry of expected) for (const candidate of entry.cards) if (candidate.id === card) {
        candidate.version = 4; candidate.updatedAt = canonical.items[0].updatedAt;
      }
      expect((await final.json()).lists).toEqual(expected);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { restoreWorker(); }
  });
}
