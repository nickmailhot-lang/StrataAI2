import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackInvitationAdmission } from './invitationAdmissionTracker';

for (const width of [1280, 390]) {
  test(`PRD-01/02/03/04-TC-03/06/11/12: invitation history rejects normalized source clocks and recovers actual records at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `history-clock-${width}-${Date.now()}@example.test`, password: 'history-clock-correct-horse', displayName: 'History clock Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'History clock council' } });
    expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private history clock Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    let documents = 0; let writes = 0;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      if (request.method() === 'DELETE' && new URL(request.url()).pathname.includes('/invitations/')) writes++;
    });
    const restore = scopedBoardWorker(org);
    try {
      for (const boardSurface of [false, true]) {
        const root = boardSurface ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
        const recipient = `clock-recipient-${boardSurface}-${width}-${Date.now()}@example.test`;
        const issued = await context.request.post(root, { headers, data: boardSurface ? { email: recipient, role: 'MEMBER' } : { email: recipient, surface: 'INTERNAL', targetRole: 'MEMBER' } });
        expect(issued.status()).toBe(201); const invitation = (await issued.json()).id;
        await waitForBoardDelivery(context.request, board);
        const before = await context.request.get(root); expect(before.status()).toBe(200); const canonical = await before.json();
        expect(canonical.items).toHaveLength(1); expect(canonical.items[0].id).toBe(invitation);
        let malformed = true; let corruptReads = 0;
        const matches = (url: URL) => url.pathname === root;
        await page.route(matches, async route => {
          if (route.request().method() !== 'GET') return route.continue();
          const reply = await route.fetch(); expect(reply.status()).toBe(200); const body = await reply.json();
          if (malformed) { corruptReads++; body.items = body.items.map((row: { id: string }) => row.id === invitation ? { ...row, expiresAt: '2035-02-30T12:00:00Z' } : row); }
          await route.fulfill({ response: reply, json: body });
        });
        const path = boardSurface ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`;
        const admitted = trackInvitationAdmission(page, org, actor, boardSurface ? board : undefined, path, root);
        await page.goto(path);
        await expect(page.getByText('Unable to confirm invitation history. Please retry.', { exact: true })).toBeVisible();
        expect(corruptReads).toBeGreaterThan(0); expect(writes).toBe(0);
        await expect(page.getByText(recipient, { exact: true })).toHaveCount(0);
        if (boardSurface) await expect(page.getByRole('heading', { name: 'Private history clock Board', exact: true })).toHaveCount(0);
        const revoke = page.getByRole('button', { name: `Revoke invitation for ${recipient}`, exact: true }); await expect(revoke).toHaveCount(0);
        malformed = false;
        await openAdmittedReview(page.getByRole('button', { name: 'Refresh invitations', exact: true }), page.getByRole('heading', { name: recipient, exact: true }));
        await expect.poll(admitted.ready, { timeout: 30_000 }).toBe(true); await expect(revoke).toBeEnabled();
        const dialog = page.getByRole('dialog', { name: 'Revoke invitation?', exact: true });
        await openAdmittedReview(revoke, dialog); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        await page.keyboard.press('Enter'); await expect(dialog).toHaveCount(0); expect(writes).toBe(0);
        const after = await context.request.get(root); expect(after.status()).toBe(200); const retained = await after.json();
        expect(retained.items).toHaveLength(1); expect(retained.nextCursor).toBe(canonical.nextCursor);
        // Mail delivery may advance independently; all source history fields
        // and clocks must remain byte-for-byte equal after display recovery.
        const sourceFields = (row: Record<string, unknown>) => Object.fromEntries(Object.entries(row).filter(([key]) => key !== 'deliveryState'));
        expect(sourceFields(retained.items[0])).toEqual(sourceFields(canonical.items[0]));
        await page.unroute(matches);
      }
      expect(documents).toBe(2); expect(writes).toBe(0);
    } finally { restore(); }
  });
}
