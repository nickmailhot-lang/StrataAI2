import { readFileSync } from 'node:fs';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

type Fixture = { organizationId: string; boardId: string; ownerEmail: string; password: string;
  formerId: string; firstPageIds: string[]; secondPageIds: string[] };
for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-05: real PostgreSQL member pages retain keyboard recovery and former-profile isolation at ${viewport.width}px`, async ({ page, context }) => {
    test.setTimeout(90_000);
    test.skip(process.env.CI !== 'true' && !process.env.STRATAAI_E2E_BOARD_MEMBER_DIRECTORY_FIXTURE,
      'Requires the isolated restricted-PostgreSQL directory fixture.');
    expect(process.env.STRATAAI_E2E_BOARD_MEMBER_DIRECTORY_FIXTURE).toBeTruthy();
    const fixture = JSON.parse(readFileSync(process.env.STRATAAI_E2E_BOARD_MEMBER_DIRECTORY_FIXTURE!, 'utf8')) as Fixture;
    expect(fixture.firstPageIds).toHaveLength(50); expect(fixture.secondPageIds).toHaveLength(3);
    expect(fixture.firstPageIds).toContain(fixture.formerId);
    const headers = { 'X-StrataAI-Request': '1' };
    expect((await context.request.post('/auth/login', { headers, data: { email: fixture.ownerEmail, password: fixture.password } })).status()).toBe(200);
    await page.setViewportSize(viewport);
    let restoreWorker = () => {};
    const memberReads: string[] = []; let writes = 0;
    page.on('request', request => {
      const url = new URL(request.url());
      if (url.pathname.startsWith(`/boards/${fixture.boardId}`) && ['PATCH', 'DELETE', 'POST'].includes(request.method())) writes++;
    });
    page.on('response', response => {
      const url = new URL(response.url());
      if (url.pathname === `/boards/${fixture.boardId}/members` && response.request().method() === 'GET' && response.status() === 200)
        memberReads.push(url.search);
    });
    try {
      restoreWorker = scopedBoardWorker(fixture.organizationId);
      await waitForBoardDelivery(context.request, fixture.boardId);
      await page.goto(`/app/${fixture.organizationId}/boards/${fixture.boardId}/members`);
      await expect(page.getByText('Live member updates connected.')).toBeVisible();
      await expect.poll(() => memberReads.length).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('progressbar', { name: 'Loading Board members' })).toHaveCount(0);
      await expect(page.getByRole('article')).toHaveCount(50);
      await expect(page.getByText(`Member reference: ${fixture.formerId}`, { exact: true })).toBeVisible();
      await expect(page.getByText(`board-page-${fixture.formerId}@example.test`, { exact: true })).toHaveCount(0);
      const former = page.getByRole('article').filter({ has: page.getByText(`Member reference: ${fixture.formerId}`, { exact: true }) });
      await expect(former.getByRole('button', { name: /Make administrator|Make member/ })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Previous members' })).toBeDisabled();
      let failedRead = false;
      await page.route(`**/boards/${fixture.boardId}`, async route => {
        if (!failedRead && route.request().method() === 'GET') {
          failedRead = true;
          await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
        } else await route.continue();
      });
      await page.getByRole('button', { name: 'Next members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/Unable to confirm current Board members/)).toBeVisible();
      await expect(page.getByRole('article')).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Paged member directory', exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Check current members' })).toBeEnabled();
      await page.getByRole('button', { name: 'Check current members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('article')).toHaveCount(3);
      expect(memberReads.at(-1)).toBe(`?after=${fixture.firstPageIds.at(-1)}`);
      await expect(page.getByRole('button', { name: 'Next members' })).toBeDisabled();
      for (const id of fixture.secondPageIds) {
        const profile = await context.request.get(`/organizations/${fixture.organizationId}/members/${id}`);
        expect(profile.status()).toBe(200);
        const current = (await profile.json()).member;
        expect(current.userId).toBe(id);
        await expect(page.getByText(current.email, { exact: true })).toBeVisible();
      }
      await page.unroute(`**/boards/${fixture.boardId}`);
      await page.getByRole('button', { name: 'Previous members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('article')).toHaveCount(50); expect(memberReads.at(-1)).toBe('');
      await expect(page.getByRole('button', { name: 'Previous members' })).toBeDisabled();
      await expect(page.getByText(`Member reference: ${fixture.formerId}`, { exact: true })).toBeVisible();
      await expect(page.getByText(`board-page-${fixture.formerId}@example.test`, { exact: true })).toHaveCount(0);
      expect(writes).toBe(0); expect(failedRead).toBe(true);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { restoreWorker(); }
  });
}
