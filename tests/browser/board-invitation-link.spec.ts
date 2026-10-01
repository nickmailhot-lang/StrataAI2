import { readFileSync } from 'node:fs';
import { expect, test } from './releaseTest';

type Fixture = { width: number; email: string; password: string; token: string; id: string;
  organizationId: string; organizationName: string; boardId: string; boardName: string; boardRole: string };
for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-05/60: keyboard Board proof review and bound retry at ${viewport.width}px`, async ({ page, context }) => {
    // This consumer fixture is created by the required restricted-PostgreSQL release step.
    // Missing preparation fails the scenario; it must never silently skip in release CI.
    test.skip(process.env.CI !== 'true' && !process.env.STRATAAI_E2E_BOARD_INVITATION_LINK_FIXTURES,
      'Requires isolated release database fixtures while public Board issuance is disabled.');
    expect(process.env.STRATAAI_E2E_BOARD_INVITATION_LINK_FIXTURES).toBeTruthy();
    const fixture = (JSON.parse(readFileSync(process.env.STRATAAI_E2E_BOARD_INVITATION_LINK_FIXTURES!, 'utf8')) as Fixture[])
      .find(row => row.width === viewport.width)!;
    expect(fixture).toBeDefined(); await page.setViewportSize(viewport);
    const urls: string[] = []; page.on('request', request => urls.push(request.url()));
    await page.goto(`/invitation#token=${fixture.token}`); await expect(page).toHaveURL(/\/invitation$/);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'StrataAI2' })).toBeVisible();
    await page.getByLabel(/^Email/).fill(fixture.email); await page.getByLabel(/^Password/).fill(fixture.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'StrataAI2' })).not.toBeVisible();
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: fixture.organizationName })).toBeVisible();
    await expect(page.getByRole('heading', { name: fixture.boardName })).toBeVisible();
    await expect(page.getByText(`Board access \u00b7 ${fixture.boardRole.toLowerCase()}`)).toBeVisible();
    const before = await context.request.get(`/boards/${fixture.boardId}`); expect(before.status()).toBe(404);
    let writes = 0;
    await page.route(`**/me/invitations/${fixture.id}/accept`, async route => {
      expect(route.request().postDataJSON()).toEqual({}); writes++;
      const response = await route.fetch(); expect(response.status()).toBe(200);
      const ack = await response.json();
      expect(ack.boardTarget).toEqual({ boardId: fixture.boardId, role: fixture.boardRole });
      if (writes === 1) await route.abort('failed'); else await route.fulfill({ response });
    });
    await page.getByRole('button', { name: 'Accept reviewed invitation' }).focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Retry invitation acceptance' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: 'Open Board' })).toHaveAttribute('href', `/app/${fixture.organizationId}/boards/${fixture.boardId}`);
    expect(writes).toBe(2);
    const after = await context.request.get(`/boards/${fixture.boardId}`); expect(after.status()).toBe(200);
    expect((await after.json()).access.canEdit).toBe(true);
    expect(urls.every(url => !url.includes(fixture.token))).toBe(true);
    expect(await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))).not.toContain(fixture.token);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
