import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';
import { trackInvitationAdmission } from './invitationAdmissionTracker';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-03/04-TC-04/06/11/12: temporary Board invitation history failure requires fresh permission before disclosure and revocation at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `history-recovery-${width}-${Date.now()}@example.test`, password: 'history-recovery-correct-horse', displayName: 'History recovery Owner' };
    const recipient = `history-recipient-${width}-${Date.now()}@example.test`;
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'History recovery council' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardName = 'Private history recovery Board';
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: boardName, visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const root = `/boards/${board}/invitations`;
    const issued = await context.request.post(root, { headers, data: { email: recipient, role: 'MEMBER' } });
    expect(issued.status()).toBe(201); const invitation = (await issued.json()).id;
    expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
    const restore = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      let refused = false; let unavailable = true; let historyReads = 0; let writes = 0; let documents = 0;
      page.on('request', request => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
        if (request.method() === 'GET' && new URL(request.url()).pathname === root) historyReads++;
        if (request.method() === 'DELETE' && new URL(request.url()).pathname === root + '/' + invitation) writes++;
      });
      await page.route(url => url.pathname === `/boards/${board}`, async route => {
        if (route.request().method() === 'GET' && unavailable) {
          refused = true;
          await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ title: 'Private storage fault', board: { name: boardName } }) });
        } else await route.continue();
      });
      const path = `/app/${org}/boards/${board}/invitations`;
      const ready = trackInvitationAdmission(page, org, actor, board, path, root);
      await page.goto(path);
      await expect(page.getByText('Unable to confirm invitation history. Please retry.', { exact: true })).toBeVisible();
      expect(refused).toBe(true); expect(historyReads).toBe(0); expect(writes).toBe(0);
      for (const value of [boardName, recipient, 'Private storage fault']) await expect(page.getByText(value, { exact: true })).toHaveCount(0);
      unavailable = false;
      const refresh = page.getByRole('button', { name: 'Refresh invitations', exact: true });
      const revoke = page.getByRole('button', { name: `Revoke invitation for ${recipient}`, exact: true });
      await openAdmittedReview(refresh, page.getByRole('heading', { name: recipient, exact: true }));
      await expect.poll(ready.ready, { timeout: 30_000 }).toBe(true); await expect(revoke).toBeEnabled(); expect(writes).toBe(0);
      const dialog = page.getByRole('dialog', { name: 'Revoke invitation?', exact: true });
      await openAdmittedReview(revoke, dialog); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.keyboard.press('Enter'); await expect(dialog).toHaveCount(0); expect(writes).toBe(0);
      await openAdmittedReview(revoke, dialog);
      await pressObservedRequest(page.getByRole('button', { name: 'Confirm revocation', exact: true }), () => writes, 1);
      await expect(page.getByText('Invitation revocation confirmed.', { exact: true })).toBeVisible(); expect(writes).toBe(1);
      const canonical = await context.request.get(root); expect(canonical.status()).toBe(200);
      const rows = (await canonical.json()).items.filter((row: { id: string }) => row.id === invitation); expect(rows).toHaveLength(1);
      expect(Number.isFinite(Date.parse(rows[0].revokedAt))).toBe(true); expect(rows[0].acceptedAt).toBeNull();
      expect(rows[0].boardTarget).toEqual({ boardId: board, role: 'MEMBER' }); expect(documents).toBe(1);
      await expect(revoke).toHaveCount(0);
    } finally { restore(); }
  });
}
