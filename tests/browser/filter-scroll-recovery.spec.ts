import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview, pressAdmittedAction } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-16-TC-05/09/11/12: filter content remains keyboard scrollable during Board recovery at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `filter-scroll-${width}-${Date.now()}@example.test`, password: 'filter-scroll-correct-horse', displayName: 'Filter keyboard reader' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'Filter keyboard council' } });
    expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Filter keyboard Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const restore = scopedBoardWorker(org);
    let release!: () => void; const pending = new Promise<void>(done => { release = done; }); let hold = false;
    try {
      await waitForBoardDelivery(context.request, board);
      const path = `/app/${org}/boards/${board}`; const reads = trackBoardReads(page, board, path); let writes = 0;
      page.on('request', request => { if (new URL(request.url()).pathname === `/boards/${board}/filter-change` && request.method() === 'POST') writes++; });
      await page.route(url => url.pathname === `/boards/${board}`, async route => {
        if (hold && route.request().method() === 'GET') await pending;
        await route.continue();
      });
      await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const dialog = page.getByRole('dialog', { name: 'Filter Board Cards', exact: true });
      await openAdmittedReview(page.getByRole('button', { name: 'Filter Board Cards', exact: true }), dialog);
      const keyword = dialog.getByRole('textbox', { name: 'Card keyword', exact: true });
      await expect(keyword).toBeEnabled(); await keyword.fill('Retained keyboard draft');
      hold = true; await context.setOffline(true); await context.setOffline(false);
      await expect(keyword).toBeDisabled();
      const content = dialog.locator('.MuiDialogContent-root');
      // Run the unchanged WCAG rule while every content control is disabled.
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await expect(content).toHaveAccessibleName('Board filter choices and results');
      await content.focus(); await expect(content).toBeFocused();
      if (width === 390) {
        expect(await content.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
        await content.press('Home'); const before = await content.evaluate(element => element.scrollTop);
        await content.press('PageDown');
        await expect.poll(() => content.evaluate(element => element.scrollTop)).toBeGreaterThan(before);
        await expect(content).toBeFocused();
      }
      const outline = await content.evaluate(element => { const style = getComputedStyle(element); return { style: style.outlineStyle, width: parseFloat(style.outlineWidth) }; });
      expect(outline.style).toBe('solid'); expect(outline.width).toBeGreaterThanOrEqual(2);
      hold = false; release(); await expect(keyword).toBeEnabled(); await expect(keyword).toHaveValue('Retained keyboard draft');
      expect(writes).toBe(0); await pressAdmittedAction(dialog.getByRole('button', { name: 'Close filters', exact: true }));
      await expect(dialog).toHaveCount(0); expect(writes).toBe(0);
    } finally { hold = false; release(); try { await context.setOffline(false); } finally { restore(); } }
  });
}
