import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-05/60: keyboard Board issuance and lost revocation recovery at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const data = { email: `board-invitation-owner-${viewport.width}-${Date.now()}@example.test`,
      password: 'browser-board-invitation-horse', displayName: 'Board invitation owner' };
    expect((await context.request.post('/auth/register', { headers, data })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Keyboard Board invitations' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Keyboard private Board' } });
    expect(created.status()).toBe(201); const board = (await created.json()).id;
    const email = `board-invitation-recipient-${viewport.width}-${Date.now()}@example.test`;
    const role = viewport.width === 1280 ? 'ADMIN' : 'MEMBER';
    await page.goto(`/app/${org}/boards/${board}`);
    await page.getByRole('link', { name: 'Invite to Board', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Create Board invitation' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Keyboard private Board' })).toBeVisible();
    await page.getByLabel(/^Invitation email/).fill(email);
    if (role === 'ADMIN') {
      await page.getByRole('combobox', { name: 'Invitation role' }).focus(); await page.keyboard.press('ArrowDown');
      await page.getByRole('option', { name: 'Admin', exact: true }).focus(); await page.keyboard.press('Enter');
    }
    const writes: { key: string | undefined; input: unknown }[] = [];
    let id = '';
    const reviewedActor = (await (await context.request.get('/me')).json()).id;
    await page.route(url => url.pathname === `/boards/${board}/invitations`, async route => {
      if (route.request().method() === 'POST')
        expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(reviewedActor);
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], input: route.request().postDataJSON() });
      const response = await route.fetch(); expect(response.status()).toBe(201);
      const acknowledgment = await response.json();
      expect(acknowledgment.boardTarget).toEqual({ boardId: board, role });
      expect(acknowledgment.invitationToken).toBeNull();
      if (id) expect(acknowledgment.id).toBe(id); else id = acknowledgment.id;
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    await page.getByRole('button', { name: 'Create invitation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/invitation could not be confirmed/)).toBeVisible();
    await page.reload(); await expect(page.getByLabel(/^Invitation email/)).toHaveValue(email);
    await page.getByRole('button', { name: 'Retry same invitation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible();
    expect(writes).toHaveLength(2); expect(writes[0]).toEqual(writes[1]);
    expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(writes[0].input).toEqual({ email, role });
    await page.getByRole('link', { name: 'Review issued invitations' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Issued Board invitations' })).toBeVisible();
    await expect(page.getByText(`Board access: ${role.toLowerCase()}`)).toBeVisible();
    const action = page.getByRole('button', { name: `Revoke invitation for ${email}` });
    await action.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused(); await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Refresh invitations' })).toBeFocused();
    let revocations = 0;
    await page.route(url => url.pathname === `/boards/${board}/invitations/${id}`, async route => {
      if (route.request().method() !== 'DELETE') { await route.continue(); return; }
      revocations++; expect((await route.fetch()).status()).toBe(204); await route.abort('timedout');
    });
    await action.focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Confirm revocation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Revocation could not be confirmed/)).toBeVisible();
    await expect(page.getByText('Invitation revocation confirmed.')).toHaveCount(0);
    await page.getByRole('button', { name: 'Check revocation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Invitation revocation confirmed.')).toBeVisible();
    await expect(page.getByText('Revoked', { exact: true })).toBeVisible(); expect(revocations).toBe(1);
    const history = await context.request.get(`/boards/${board}/invitations`); expect(history.status()).toBe(200);
    const rows = (await history.json()).items; expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ id, email, boardTarget: { boardId: board, role } }); expect(rows[0].revokedAt).not.toBeNull();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
