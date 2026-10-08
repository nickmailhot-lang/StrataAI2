import { expect, test } from './releaseTest';
import { trackBoardReads, waitForBoardReads } from './boardReadTracker';
import { focusAdmittedControl, pressAdmittedAction } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardHistoryChanges, trackInvitationAdmission } from './invitationAdmissionTracker';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-05/60: keyboard Board issuance and lost revocation recovery at ${viewport.width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize(viewport);
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
    let restoreWorker = () => {};
    try {
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const reviewedActor = (await (await context.request.get('/me')).json()).id;
      const creationAdmission = trackInvitationAdmission(page, org, reviewedActor, board, `/app/${org}/boards/${board}/invite`);
      const historyChanges = trackBoardHistoryChanges(page, org, board, `/app/${org}/boards/${board}/invitations`);
      await page.goto(`/app/${org}/boards/${board}`);
      const admittedReads = trackBoardReads(page, board, `/app/${org}/boards/${board}/invite`);
      await pressAdmittedAction(page.getByRole('link', { name: 'Invite to Board', exact: true }));
      await expect(page.getByRole('heading', { name: 'Create Board invitation' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Keyboard private Board' })).toBeVisible();
      // Complete the initial live-head permission refresh before composing. A
      // background read may temporarily disable the keyboard submit control.
      await waitForBoardReads(page, admittedReads, 2);
      await expect.poll(creationAdmission.ready).toBe(true);
      await expect(page.getByRole('button', { name: 'Create invitation', exact: true })).toBeEnabled();
      await page.getByLabel(/^Invitation email/).fill(email);
      if (role === 'ADMIN') {
        const roleChoice = page.getByRole('combobox', { name: 'Invitation role' }); await focusAdmittedControl(roleChoice); await roleChoice.press('ArrowDown');
        await pressAdmittedAction(page.getByRole('option', { name: 'Admin', exact: true }));
      }
      const writes: { key: string | undefined; input: unknown }[] = [];
      let id = '';
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
      await expect(page.getByLabel(/^Invitation email/)).toHaveValue(email);
      await pressAdmittedAction(page.getByRole('button', { name: 'Create invitation', exact: true }));
      await expect(page.getByText(/invitation could not be confirmed/)).toBeVisible();
      const beforeReload = admittedReads(), beforeHead = creationAdmission.heads();
      await page.reload(); await waitForBoardReads(page, admittedReads, beforeReload + 2);
      await expect.poll(() => creationAdmission.heads() > beforeHead && creationAdmission.ready()).toBe(true);
      await expect(page.getByLabel(/^Invitation email/)).toHaveValue(email);
      await expect(page.getByRole('button', { name: 'Retry same invitation' })).toBeEnabled();
      await pressAdmittedAction(page.getByRole('button', { name: 'Retry same invitation' }));
      await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[0]).toEqual(writes[1]);
      expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(writes[0].input).toEqual({ email, role });
      await waitForBoardDelivery(context.request, board);
      await pressAdmittedAction(page.getByRole('link', { name: 'Review issued invitations' }));
      await expect(page.getByRole('heading', { name: 'Issued Board invitations' })).toBeVisible();
      await expect(page.getByText(`Board access: ${role.toLowerCase()}`)).toBeVisible();
      // Observe the real committed issuance and a history read after its frame
      // before opening consent, including canonical replay on this new page.
      await expect.poll(() => historyChanges.settled('BOARD_MEMBER_INVITED', 1)).toBe(true);
      const action = page.getByRole('button', { name: `Revoke invitation for ${email}` });
      await pressAdmittedAction(action);
      await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused(); await page.getByRole('button', { name: 'Cancel', exact: true }).press('Enter');
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Refresh invitations' })).toBeFocused();
      let revocations = 0;
      await page.route(url => url.pathname === `/boards/${board}/invitations/${id}`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        revocations++; expect((await route.fetch()).status()).toBe(204); await route.abort('timedout');
      });
      await pressAdmittedAction(action);
      await pressAdmittedAction(page.getByRole('button', { name: 'Confirm revocation' }));
      await expect(page.getByText(/Revocation could not be confirmed/)).toBeVisible();
      await expect(page.getByText('Invitation revocation confirmed.')).toHaveCount(0);
      await pressAdmittedAction(page.getByRole('button', { name: 'Check revocation' }));
      await expect(page.getByText('Invitation revocation confirmed.')).toBeVisible();
      await expect(page.getByText('Revoked', { exact: true })).toBeVisible(); expect(revocations).toBe(1);
      const history = await context.request.get(`/boards/${board}/invitations`); expect(history.status()).toBe(200);
      const rows = (await history.json()).items; expect(rows).toHaveLength(1);
      expect(rows[0]).toMatchObject({ id, email, boardTarget: { boardId: board, role } }); expect(rows[0].revokedAt).not.toBeNull();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { restoreWorker(); }
  });
}
