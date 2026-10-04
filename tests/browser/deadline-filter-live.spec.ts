import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-16 deadline filters recover real completion and missed updates at ${width}px`, async ({ page, context }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `deadline-filter-${width}-${Date.now()}@example.test`, password: 'deadline-filter-correct-horse', displayName: 'Deadline filter reader' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Deadline filter delivery' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Deadline states', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const createdList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
    const createdCard = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Live deadline Card' } });
    expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
    let version = 1; let restoreWorker = () => {};
    async function dates(dueAt: string | null, dueComplete = false) {
      const reply = await context.request.patch(`/cards/${card}/dates`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { dueAt, dueTimezone: dueAt ? 'UTC' : null, dueHasTime: dueAt !== null, dueComplete, version },
      });
      expect(reply.status()).toBe(200); version = (await reply.json()).card.version;
    }
    try {
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const path = `/app/${org}/boards/${board}`; const reads = trackBoardReads(page, board, path);
      await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await page.getByRole('button', { name: 'Filter Board Cards', exact: true }).press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Filter Board Cards', exact: true });
      async function select(label: string, option: string) {
        const field = dialog.getByRole('combobox', { name: label, exact: true }); await expect(field).toBeEnabled();
        await field.press('Enter'); await page.getByRole('option', { name: option, exact: true }).press('Enter');
        await dialog.getByRole('button', { name: 'Apply filters', exact: true }).press('Enter');
      }
      await select('Deadline state', 'Upcoming');
      const match = dialog.getByRole('link', { name: 'Live deadline Card — Planning', exact: true });
      const empty = dialog.getByText('No Cards match these filters.', { exact: true });
      await expect(empty).toBeVisible();
      const future = new Date(Date.now() + 86_400_000).toISOString();
      await dates(future); await expect(match).toBeVisible({ timeout: 20_000 });
      await dates(future, true); await expect(empty).toBeVisible({ timeout: 20_000 });
      await dates(future); await expect(match).toBeVisible({ timeout: 20_000 });
      await context.setOffline(true);
      // APIRequestContext performs the independent mutation while the browser
      // is offline. Drain real delivery before reconnect, forcing missed state.
      await dates(new Date(Date.now() - 86_400_000).toISOString());
      await waitForBoardDelivery(context.request, board); await context.setOffline(false);
      await expect(empty).toBeVisible({ timeout: 30_000 });
      await select('Deadline state', 'Overdue'); await expect(match).toBeVisible();
      await select('Recent Card updates', 'Last 24 hours'); await expect(match).toBeVisible();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await dialog.getByRole('button', { name: 'Show this page on Board', exact: true }).press('Enter');
      await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
      await page.reload(); await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
      await dates(null); await expect(page.getByText('Filtered Board: 0 matching Cards on this page.', { exact: true })).toBeVisible({ timeout: 20_000 });
      const retained = await (await context.request.get(`/boards/${board}`)).json();
      const canonical = retained.lists.flatMap((column: { cards: { id: string; version: number; dueAt: string | null }[] }) => column.cards).find((row: { id: string }) => row.id === card);
      expect(canonical).toMatchObject({ id: card, version, dueAt: null });
    } finally { try { await context.setOffline(false); } finally { restoreWorker(); } }
  });
}
