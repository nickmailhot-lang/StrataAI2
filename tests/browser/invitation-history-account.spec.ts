import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { trackInvitationAdmission } from './invitationAdmissionTracker';

for (const width of [1280, 390]) for (const boardSurface of [false, true]) {
  test(`PRD-03/05/60: account uncertainty before and after revocation (Board=${boardSurface}, ${width}px)`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${boardSurface}-${width}-${Date.now()}`;
    const credentials = { email: `history-account-owner-${suffix}@example.test`, password: 'history-account-correct-horse', displayName: 'History reviewer' };
    const registered = await context.request.post('/auth/register', { headers, data: credentials });
    expect(registered.status()).toBe(201); const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Reviewed history account' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    let board: string | undefined;
    if (boardSurface) {
      const result = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private account history Board' } });
      expect(result.status()).toBe(201); board = (await result.json()).id;
    }
    const root = board ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
    const path = board ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`;
    const admission = trackInvitationAdmission(page, org, actor, board, path, root);
    const email = `history-account-recipient-${suffix}@example.test`;
    const invitation = await context.request.post(root, { headers, data: board ? { email, role: 'MEMBER' }
      : { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201); const id = (await invitation.json()).id;
    const canonical = await context.request.get(root); expect(canonical.status()).toBe(200); const before = await canonical.json();
    let failProfile = false, writes = 0, documents = 0;
    await page.route(url => url.pathname === '/me', async route => {
      if (!failProfile) { await route.continue(); return; }
      failProfile = false; await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
    });
    await page.route(url => url.pathname === `${root}/${id}`, async route => {
      if (route.request().method() !== 'DELETE') { await route.continue(); return; }
      expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(actor);
      writes++; const response = await route.fetch(); expect(response.status()).toBe(204);
      failProfile = true; await route.fulfill({ response });
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    await page.goto(path);
    // First displayed history can precede the watcher bootstrap invalidation.
    // Wait for the actual scoped head and a protected history read after it.
    await expect.poll(admission.ready).toBe(true);
    const review = page.getByRole('button', { name: `Revoke invitation for ${email}` });
    await review.click(); await expect(page.getByRole('dialog')).toBeVisible();
    failProfile = true; await page.getByRole('button', { name: 'Confirm revocation' }).click();
    await expect(page.getByText('Your account could not be confirmed. No revocation was sent. Refresh invitations before reviewing again.')).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByRole('heading', { name: email })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Check revocation' })).toHaveCount(0); expect(writes).toBe(0);
    const unchanged = await context.request.get(root); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(before);
    await page.getByRole('button', { name: 'Refresh invitations' }).click(); await expect(review).toBeVisible(); expect(writes).toBe(0);
    await review.click(); await page.getByRole('button', { name: 'Confirm revocation' }).click();
    await expect(page.getByText('Revocation could not be confirmed. Check the current invitation state before another action.')).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByRole('heading', { name: email })).toHaveCount(0);
    await expect(page.getByText('Invitation revocation confirmed.')).toHaveCount(0); expect(writes).toBe(1);
    if (board) await expect(page.getByRole('heading', { name: 'Private account history Board' })).toHaveCount(0);
    const committed = await context.request.get(root); expect(committed.status()).toBe(200); const after = await committed.json();
    expect(after.items.find((row: { id: string; revokedAt: string | null }) => row.id === id).revokedAt).not.toBeNull();
    await page.getByRole('button', { name: 'Check revocation' }).click();
    await expect(page.getByText('Invitation revocation confirmed.')).toBeVisible(); await expect(page.getByText('Revoked', { exact: true })).toBeVisible();
    expect(writes).toBe(1); expect(documents).toBe(1);
    const recovered = await context.request.get(root); expect(recovered.status()).toBe(200); expect(await recovered.json()).toEqual(after);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
