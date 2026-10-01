import { readFileSync } from 'node:fs';
import { expect, test } from './releaseTest';

type Fixture = { width: number; email: string; password: string; token: string; id: string; organizationId: string; surface: string; organizationName: string };
for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-60: keyboard invitation signup retries before explicit acceptance at ${viewport.width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize(viewport); const headers = { 'X-StrataAI-Request': '1' }; let fixture: Fixture;
    if (process.env.CI === 'true') {
      expect(process.env.STRATAAI_E2E_INVITATION_SIGNUP_FIXTURES).toBeTruthy();
      fixture = (JSON.parse(readFileSync(process.env.STRATAAI_E2E_INVITATION_SIGNUP_FIXTURES!, 'utf8')) as Fixture[]).find(row => row.width === viewport.width)!;
      expect(fixture).toBeDefined();
    } else {
      const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
      try {
        const password = 'signup-correct-horse-battery'; const email = `browser-signup-${viewport.width}-${Date.now()}@example.test`;
        const data = { email: `browser-signup-owner-${viewport.width}-${Date.now()}@example.test`, password, displayName: 'Signup owner' };
        expect((await owner.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await owner.request.post('/auth/login', { headers, data })).status()).toBe(200);
        const org = await owner.request.post('/organizations', { headers, data: { name: 'Closed invitation signup' } }); expect(org.status()).toBe(201);
        const organizationId = (await org.json()).organization.id; const surface = viewport.width === 390 ? 'PORTAL' : 'INTERNAL';
        const issued = await owner.request.post(`/organizations/${organizationId}/invitations`, { headers, data: { email, surface, targetRole: surface === 'PORTAL' ? 'OWNER' : 'MEMBER' } });
        expect(issued.status()).toBe(201); const invitation = await issued.json(); expect(invitation.invitationToken).toBeTruthy();
        fixture = { width: viewport.width, email, password, token: invitation.invitationToken, id: invitation.id, organizationId, surface, organizationName: 'Closed invitation signup' };
      } finally { await owner.close(); }
    }
    await page.goto(`/invitation#token=${fixture.token}`); await expect(page).toHaveURL(/\/invitation$/);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await page.getByRole('tab', { name: 'Register' }).focus(); await page.keyboard.press('Enter');
    await page.getByLabel(/^Display name/).fill('Invited browser recipient'); await page.getByLabel(/^Email/).fill(fixture.email); await page.getByLabel(/^Password/).fill(fixture.password);
    const writes: { body: string | null; key: string | undefined }[] = [];
    await page.route('**/auth/register', async route => {
      writes.push({ body: route.request().postData(), key: route.request().headers()['idempotency-key'] });
      expect(route.request().postDataJSON().invitationToken).toBe(fixture.token);
      const response = await route.fetch(); expect(response.status()).toBe(201);
      if (writes.length === 1) await route.abort('failed'); else await route.fulfill({ response });
    });
    await page.getByRole('button', { name: 'Create account' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Registration could not be confirmed/)).toBeVisible();
    await page.getByRole('button', { name: 'Create account' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Account created. Sign in to continue.')).toBeVisible();
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
    await page.getByLabel(/^Password/).fill(fixture.password); await page.getByRole('button', { name: 'Sign in', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'StrataAI2' })).not.toBeVisible();
    const pending = await context.request.get('/me/invitations'); expect(pending.status()).toBe(200);
    expect((await pending.json()).items.some((row: { id: string }) => row.id === fixture.id)).toBe(true);
    const discovery = await context.request.get('/organizations'); expect(discovery.status()).toBe(200);
    expect((await discovery.json()).some((row: { organization: { id: string } }) => row.organization.id === fixture.organizationId)).toBe(false);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: fixture.organizationName })).toBeVisible();
    await page.getByRole('button', { name: 'Accept reviewed invitation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: fixture.surface === 'PORTAL' ? 'Open Owner Portal' : 'Open organization' }))
      .toHaveAttribute('href', `${fixture.surface === 'PORTAL' ? '/portal' : '/app'}/${fixture.organizationId}`);
  });
}
