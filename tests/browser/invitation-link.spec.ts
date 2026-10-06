import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { expect, test } from './releaseTest';

type Fixture = { width: number; email: string; password: string; token: string; id: string; organizationId: string; surface: string; organizationName: string };
for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-60: keyboard invitation link review and lost acknowledgment at ${viewport.width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize(viewport);
    let fixture: Fixture;
    if (process.env.CI === 'true') {
      expect(process.env.STRATAAI_E2E_INVITATION_LINK_FIXTURES).toBeTruthy();
      const fixtures = JSON.parse(readFileSync(process.env.STRATAAI_E2E_INVITATION_LINK_FIXTURES!, 'utf8')) as Fixture[];
      fixture = fixtures.find(row => row.width === viewport.width)!; expect(fixture).toBeDefined();
    } else {
      const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
      const headers = { 'X-StrataAI-Request': '1' }; const password = 'link-correct-horse-battery';
      const email = `link-recipient-${viewport.width}-${Date.now()}@example.test`;
      try {
        const ownerInput = { email: `link-owner-${viewport.width}-${Date.now()}@example.test`, password, displayName: 'Link owner' };
        expect((await owner.request.post('/auth/register', { headers, data: ownerInput })).status()).toBe(201);
        expect((await owner.request.post('/auth/login', { headers, data: ownerInput })).status()).toBe(200);
        expect((await context.request.post('/auth/register', { headers, data: { email, password, displayName: 'Link recipient' } })).status()).toBe(201);
        const created = await owner.request.post('/organizations', { headers, data: { name: 'Body acceptance fixture' } }); expect(created.status()).toBe(201);
        const organizationId = (await created.json()).organization.id;
        const surface = viewport.width === 390 ? 'PORTAL' : 'INTERNAL';
        const issued = await owner.request.post(`/organizations/${organizationId}/invitations`, { headers, data: { email, surface, targetRole: surface === 'PORTAL' ? 'OWNER' : 'MEMBER' } });
        expect(issued.status()).toBe(201); const invitation = await issued.json(); expect(invitation.invitationToken).toBeTruthy();
        fixture = { width: viewport.width, email, password, token: invitation.invitationToken, id: invitation.id, organizationId, surface, organizationName: 'Body acceptance fixture' };
      } finally { await owner.close(); }
    }
    test.setTimeout(120_000);
    const urls: string[] = []; page.on('request', request => urls.push(request.url()));
    await page.goto(`/invitation#token=${fixture.token}`);
    await expect(page).toHaveURL(/\/invitation$/);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'StrataAI2' })).toBeVisible();
    await page.getByLabel(/^Email/).fill(fixture.email); await page.getByLabel(/^Password/).fill(fixture.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'StrataAI2' })).not.toBeVisible();
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: fixture.organizationName })).toBeVisible();
    const accountHeaders = { 'X-StrataAI-Request': '1' };
    const actor = (await (await context.request.get('/me')).json()).id;
    const replacement = { email: `link-replacement-${viewport.width}-${Date.now()}@example.test`, password: 'link-account-correct-horse', displayName: 'Replacement link account' };
    expect((await context.request.post('/auth/register', { headers: accountHeaders, data: replacement })).status()).toBe(201);
    let profileUnavailable = false;
    await page.route(url => url.pathname === '/me', async route => {
      if (!profileUnavailable) { await route.continue(); return; }
      profileUnavailable = false;
      await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"session_unavailable"}' });
    });
    async function originalSignIn() {
      await expect(page.getByRole('heading', { name: 'StrataAI2' })).toBeVisible();
      await page.getByLabel(/^Email/).fill(fixture.email); await page.getByLabel(/^Password/).fill(fixture.password);
      await page.getByRole('button', { name: 'Sign in', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('heading', { name: 'StrataAI2' })).not.toBeVisible();
    }
    let acknowledged: unknown;
    let writes = 0;
    await page.route(url => url.pathname === `/me/invitations/${fixture.id}/accept`, async route => {
      expect(route.request().postDataJSON()).toEqual({});
      writes++; const response = await route.fetch(); expect(response.status()).toBe(200);
      const retainedAck = await response.json();
      if (writes === 1) acknowledged = retainedAck; else expect(retainedAck).toEqual(acknowledged);
      expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(actor);
      if (writes === 1) await route.abort('failed');
      else {
        if (writes === 2) {
          if (viewport.width === 1280) expect((await context.request.post('/auth/login', { headers: accountHeaders, data: replacement })).status()).toBe(200);
          else profileUnavailable = true;
        }
        await route.fulfill({ response });
      }
    });
    // Actual cookie change before submission cannot reuse the reviewed consent.
    expect((await context.request.post('/auth/login', { headers: accountHeaders, data: replacement })).status()).toBe(200);
    await page.getByRole('button', { name: 'Accept reviewed invitation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: fixture.organizationName })).toHaveCount(0);
    await originalSignIn(); expect(writes).toBe(0);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: fixture.organizationName })).toBeVisible();
    await page.getByRole('button', { name: 'Accept reviewed invitation' }).focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Retry invitation acceptance' }).focus(); await page.keyboard.press('Enter');
    // The second real receipt is withheld after account replacement/uncertainty.
    if (viewport.width === 1280) await originalSignIn();
    else await expect(page.getByText('The reviewed account could not be confirmed. Retry after account access is available.', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: fixture.organizationName })).toHaveCount(0);
    await expect(page.getByRole('link', { name: /^Open / })).toHaveCount(0); expect(writes).toBe(2);
    await page.getByRole('button', { name: 'Retry invitation acceptance' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: fixture.surface === 'PORTAL' ? 'Open Owner Portal' : 'Open organization' }))
      .toHaveAttribute('href', `${fixture.surface === 'PORTAL' ? '/portal' : '/app'}/${fixture.organizationId}`);
    expect(writes).toBe(3); expect(urls.every(url => !url.includes(fixture.token))).toBe(true);
    expect(await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }))).not.toContain(fixture.token);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
