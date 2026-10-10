import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { pressAdmittedAction } from './keyboardAdmission';
import { trackBoardReads } from './boardReadTracker';

// FOUND-FR-008 / PRD-01-TC-12 / PRD-04: intentional canvas scrolling must not
// conceal a clipped canonical Card title or an oversized List action.
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-01/04: Board and Card reflow with long canonical labels at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const boardName = 'B'.repeat(100), listName = 'L'.repeat(100), cardTitle = 'W'.repeat(100);
    await registerNotificationAccount(context.request, {
      email: `board-reflow-${width}-${Date.now()}@example.test`, password: 'board-reflow-correct-horse', displayName: 'Board reflow owner',
    });
    const orgResponse = await context.request.post('/organizations', { headers, data: { name: 'Board reflow Organization' } });
    expect(orgResponse.status()).toBe(201); const org = (await orgResponse.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: boardName, visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: listName } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: cardTitle } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const restore = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const path = `/app/${org}/boards/${board}`;
      const reflow = async () => {
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
        const dialog = page.getByRole('dialog');
        if (await dialog.count()) expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      };
      const reads = trackBoardReads(page, board, path);
      await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      await expect(page.getByRole('heading', { name: boardName, exact: true })).toBeVisible();
      const title = page.getByRole('link', { name: cardTitle, exact: true });
      await expect(title).toBeVisible(); await expect(title.locator('.MuiCardContent-root')).toHaveText(cardTitle); await reflow();
      expect(await title.locator('.MuiCardContent-root').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      const column = page.getByRole('region', { name: listName, exact: true });
      const endTarget = column.locator('[data-card-list-end]');
      await expect(endTarget).toHaveText(`Drop card at end of ${listName}`);
      expect(await endTarget.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      expect(await column.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      // A section with its heading is a named region; check both intrinsic text
      // overflow and the action's bounds inside the fixed canvas column.
      for (const name of [`Drag ${listName} list`, `Add card to ${listName}`, `Move ${listName} list`, `Drag ${cardTitle} card`]) {
        const action = page.getByRole('button', { name, exact: true }); await expect(action).toBeEnabled();
        expect(await action.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
        const actionBox = await action.boundingBox(); const columnBox = await column.boundingBox();
        expect(actionBox).not.toBeNull(); expect(columnBox).not.toBeNull();
        expect(actionBox!.width).toBeLessThanOrEqual(columnBox!.width);
      }
      await pressAdmittedAction(page.getByRole('button', { name: 'Edit Board details', exact: true }));
      await expect(page.getByLabel('Board name', { exact: true })).toHaveValue(boardName); await reflow();
      await pressAdmittedAction(page.getByRole('button', { name: 'Cancel Board changes', exact: true }));
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await pressAdmittedAction(page.getByRole('button', { name: `Rename ${listName} list`, exact: true }));
      await expect(page.getByLabel(/^New list name/)).toHaveValue(listName); await reflow();
      await pressAdmittedAction(page.getByRole('button', { name: 'Cancel rename', exact: true }));
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await title.focus(); await expect(title).toBeFocused(); await title.press('Enter');
      await expect(page).toHaveURL(new RegExp(`/cards/${card}$`));
      await expect(page.getByRole('dialog', { name: 'Card details', exact: true })).toBeVisible();
      await expect(page.getByLabel(/^Card title/)).toHaveValue(cardTitle); await reflow();
      await pressAdmittedAction(page.getByRole('button', { name: 'Close', exact: true }));
      await expect(page).toHaveURL(new RegExp(`/boards/${board}$`)); await expect(title).toBeFocused(); await reflow();
      const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
      expect(await current.json()).toMatchObject({ board: { name: boardName },
        lists: [{ list: { id: list, name: listName }, cards: [{ id: card, title: cardTitle }] }] });
    } finally { restore(); }
  });
}
