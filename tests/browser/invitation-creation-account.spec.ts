import { expect, test } from './releaseTest';
import { trackInvitationAdmission } from './invitationAdmissionTracker';

for (const width of [1280, 390]) for (const boardSurface of [false, true]) for (const fault of ['replacement', 'unavailable']) {
  test(`PRD-03/60-TC-05/06/07: committed invitation is withheld after account ${fault} (Board=${boardSurface}, ${width}px)`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${fault}-${boardSurface}-${width}-${Date.now()}`;
    const owner = { email: `creation-owner-${suffix}@example.test`, password: 'creation-account-correct-horse', displayName: 'Invitation reviewer' };
    const other = { ...owner, email: `creation-other-${suffix}@example.test`, displayName: 'Replacement account' };
    const registered = await context.request.post('/auth/register', { headers, data: owner });
    expect(registered.status()).toBe(201); const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/register', { headers, data: other })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Creation account boundary' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    let board: string | undefined;
    if (boardSurface) {
      const result = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private reviewed Board' } });
      expect(result.status()).toBe(201); board = (await result.json()).id;
    }
    const root = board ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
    const path = board ? `/app/${org}/boards/${board}/invite` : `/app/${org}/invite`;
    const admission = trackInvitationAdmission(page, org, actor, board, path);
    const storage = `strataai:invitation-create:v1:${actor}:${org}${board ? `:board:${board}` : ''}`;
    const email = `creation-recipient-${suffix}@example.test`;
    const writes: { key: string | undefined; body: unknown }[] = [];
    let original: Record<string, unknown> | undefined;
    let profileUnavailable = false;
    await page.route(url => url.pathname === '/me', async route => {
      if (!profileUnavailable) { await route.continue(); return; }
      profileUnavailable = false;
      // Inject only account-read transport failure after the real command has
      // committed. No invitation, grant, receipt or acknowledgment is fabricated.
      await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"session_unavailable"}' });
    });
    await page.route(url => url.pathname === root, async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(actor);
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postDataJSON() });
      const response = await route.fetch(); expect(response.status()).toBe(201);
      const acknowledgment = await response.json();
      if (writes.length === 1) {
        original = acknowledgment;
        if (fault === 'replacement')
          expect((await context.request.post('/auth/login', { headers, data: other })).status()).toBe(200);
        else profileUnavailable = true;
      } else expect(acknowledgment).toEqual(original);
      await route.fulfill({ response });
    });
    await page.goto(path);
    if (!boardSurface) await expect(page.getByText('Current invitation permissions checked. Review the request before submitting.', { exact: true })).toBeVisible();
    await expect.poll(admission.ready).toBe(true);
    await expect(page.getByRole('button', { name: 'Create invitation', exact: true })).toBeEnabled();
    const originalHead = admission.heads();
    await page.getByLabel(/^Invitation email/).fill(email);
    await page.getByRole('button', { name: 'Create invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    if (fault === 'replacement') await expect(page).toHaveURL(/\/login(?:\?|$)/);
    else {
      await expect(page.getByRole('button', { name: 'Retry permission check' })).toBeEnabled();
      await expect(page.getByLabel(/^Invitation email/)).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'Private reviewed Board', exact: true })).toHaveCount(0);
    }
    await expect(page.getByText('Invitation creation acknowledged.', { exact: true })).toHaveCount(0);
    await expect(page.getByText(email, { exact: true })).toHaveCount(0);
    expect(writes).toHaveLength(1);
    const retained = await page.evaluate(key => sessionStorage.getItem(key), storage);
    expect(JSON.parse(retained!).key).toBe(writes[0].key);
    if (fault === 'replacement') {
      expect((await context.request.get(root)).status()).toBe(404);
      expect((await context.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      await page.goto(path);
    } else {
      const persisted = await context.request.get(root); expect(persisted.status()).toBe(200);
      expect((await persisted.json()).items).toHaveLength(1);
      await page.getByRole('button', { name: 'Retry permission check' }).focus(); await page.keyboard.press('Enter');
    }
    // Recovery starts a new account-bound watcher. Wait for its actual head and
    // the fresh permission read, rather than racing the first restored form.
    await expect.poll(() => admission.heads() > originalHead && admission.ready()).toBe(true);
    await expect(page.getByText(/prior invitation request is awaiting acknowledgment/)).toBeVisible();
    expect(writes).toHaveLength(1);
    await expect(page.getByRole('button', { name: 'Retry same invitation' })).toBeEnabled();
    await page.getByRole('button', { name: 'Retry same invitation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Invitation creation acknowledged.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
    const history = await context.request.get(root); expect(history.status()).toBe(200);
    const rows = (await history.json()).items;
    expect(rows).toHaveLength(1); expect(rows[0].id).toBe(original!.id);
  });
}
