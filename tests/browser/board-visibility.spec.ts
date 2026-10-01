import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-05: keyboard visibility consent, conflict and public read-only recovery at ${viewport.width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000);
    await page.setViewportSize(viewport);
    let restoreWorker = () => {};
    const headers = { 'X-StrataAI-Request': '1' };
    const data = { email: `visibility-owner-${viewport.width}-${Date.now()}@example.test`, password: 'browser-visibility-correct-horse', displayName: 'Visibility owner' };
    expect((await context.request.post('/auth/register', { headers, data })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Visibility consent' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Visibility review', visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201); const board = (await created.json()).id;
    const initial = await context.request.get(`/boards/${board}`); expect(initial.status()).toBe(200);
    const version = (await initial.json()).board.version;
    const anonymous = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    try {
      expect((await anonymous.request.get(`/boards/${board}`)).status()).toBe(404);
      restoreWorker = scopedBoardWorker(org);
      await waitForBoardDelivery(context.request, board);
      await page.goto(`/app/${org}/boards/${board}`);
      let visibilityReads = 0;
      page.on('response', response => {
        if (new URL(response.url()).pathname === `/boards/${board}` && response.request().method() === 'GET' && response.status() === 200) visibilityReads++;
      });
      await page.getByRole('link', { name: 'Board visibility', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Live visibility updates connected.')).toBeVisible();
      await expect.poll(() => visibilityReads).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('progressbar', { name: 'Checking Board visibility' })).toHaveCount(0);
      const choosePublic = async () => {
        await page.getByRole('combobox', { name: 'Board visibility' }).focus(); await page.keyboard.press('ArrowDown');
        await page.getByRole('option', { name: 'Public', exact: true }).focus(); await page.keyboard.press('Enter');
        await page.getByRole('button', { name: 'Review visibility change' }).focus(); await page.keyboard.press('Enter');
        await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
        await expect(page.getByText(/Anyone, including people who are not signed in/)).toBeVisible();
      };
      await choosePublic(); await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Review visibility change' })).toBeFocused();
      expect((await anonymous.request.get(`/boards/${board}`)).status()).toBe(404);
      let nextVersion = version; let writes = 0;
      await page.route(`**/boards/${board}/visibility`, async route => {
        const input = route.request().postDataJSON(); writes++;
        if (writes === 1) {
          const competing = await context.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility: 'ORGANIZATION', version } });
          expect(competing.status()).toBe(200); nextVersion = (await competing.json()).version;
        }
        expect(input).toEqual({ visibility: 'PUBLIC', version: writes === 1 ? version : nextVersion });
        expect(route.request().headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
        const result = await route.fetch(); expect(result.status()).toBe(writes === 1 ? 409 : 200);
        if (writes === 1) await route.fulfill({ response: result }); else await route.abort('timedout');
      });
      await page.getByRole('button', { name: 'Review visibility change' }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Confirm visibility change' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/The Board changed/)).toBeVisible();
      // A live canonical read may already have restored authorized metadata.
      await expect(page.getByRole('button', { name: 'Check current visibility' })).toBeEnabled();
      await page.getByRole('button', { name: 'Check current visibility' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('combobox', { name: 'Board visibility' })).toHaveText('Organization');
      await choosePublic();
      await page.getByRole('button', { name: 'Confirm visibility change' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/Unable to confirm current Board visibility/)).toBeVisible();
      await page.getByRole('button', { name: 'Check current visibility' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('combobox', { name: 'Board visibility' })).toHaveText('Public'); expect(writes).toBe(2);
      const publicLink = page.getByRole('textbox', { name: 'Public Board link' });
      await expect(publicLink).toHaveValue(new URL(`/app/${org}/boards/${board}`, test.info().project.use.baseURL).href);
      await publicLink.focus(); await page.keyboard.press('ControlOrMeta+A');
      const visitor = await anonymous.newPage(); await visitor.setViewportSize(viewport);
      await visitor.goto(await publicLink.inputValue());
      await expect(visitor.getByRole('heading', { name: 'Visibility review', exact: true })).toBeVisible();
      await expect(visitor.getByRole('button', { name: 'Add list', exact: true })).toHaveCount(0);
      await expect(visitor.getByRole('link', { name: 'Board visibility', exact: true })).toHaveCount(0);
      await expect(visitor.getByRole('link', { name: 'Board members', exact: true })).toHaveCount(0);
      expect(await visitor.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await visitor.close();
      const publicRead = await anonymous.request.get(`/boards/${board}`); expect(publicRead.status()).toBe(200);
      expect((await publicRead.json()).access).toMatchObject({ canView: true, canEdit: false, canMove: false, canAdminister: false });
      expect((await anonymous.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility: 'PRIVATE', version: nextVersion + 1 } })).status()).toBe(401);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { try { restoreWorker(); } finally { await anonymous.close(); } }
  });
}
