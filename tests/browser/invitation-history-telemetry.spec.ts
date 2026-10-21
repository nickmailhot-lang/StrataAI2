import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview } from './keyboardAdmission';
import type { Locator } from '@playwright/test';
import { trackInvitationAdmission } from './invitationAdmissionTracker';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

// A live permission read can retire consent between opening and focusing it.
// Reopen a current review only before the first observed DELETE. Every native
// action retains the original500ms bound and the whole admission remains5s.
async function confirmCurrentReview(trigger: Locator, review: Locator, confirm: Locator, writes: () => number, expected: number) {
  expect(writes()).toBe(expected - 1);
  await expect(async () => {
    if (writes() === expected - 1) {
      if (!await review.isVisible()) {
        await expect(trigger).toBeEnabled({ timeout: 500 });
        await trigger.focus({ timeout: 500 }); await expect(trigger).toBeFocused({ timeout: 500 });
        await expect(trigger).toBeEnabled({ timeout: 500 });
        if (writes() === expected - 1) await trigger.press('Enter', { timeout: 500 });
      }
      if (writes() === expected - 1) {
        await expect(review).toBeVisible({ timeout: 500 });
        await expect(confirm).toBeEnabled({ timeout: 500 });
        await confirm.focus({ timeout: 500 }); await expect(confirm).toBeFocused({ timeout: 500 });
        await expect(confirm).toBeEnabled({ timeout: 500 });
        if (writes() === expected - 1) await confirm.press('Enter', { timeout: 500 });
      }
    }
    await expect.poll(writes, { timeout: 500 }).toBe(expected);
  }).toPass({ timeout: 5_000 });
}

for (const width of [1280, 390]) {
  test(`PRD-03/04/60-TC-01/11/12: invitation history and confirmed revocation report private-data-free outcomes at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `history-metrics-${width}-${Date.now()}@example.test`, password: 'history-metrics-correct-horse', displayName: 'Private history metric Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201); const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'Private metrics council' } });
    expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private history metrics Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const reports: unknown[] = []; const events: { action: string; kind: string; count: number; durationMs?: number }[] = []; const statuses: number[] = [];
    const privateValues = [org, board, actor, account.email, account.password, account.displayName, 'Private metrics council', 'Private history metrics Board'];
    let writes = 0; let documents = 0;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      if (request.method() === 'DELETE' && new URL(request.url()).pathname.includes('/invitations/')) writes++;
      if (request.method() === 'POST' && new URL(request.url()).pathname === '/me/activity-client-events') {
        const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
      }
    });
    page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
    const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
    const restore = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      for (const boardSurface of [false, true]) {
        const prefix = boardSurface ? 'board_invitation_history' : 'organization_invitation_history';
        const root = boardSurface ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
        const recipient = `private-history-${boardSurface}-${width}-${Date.now()}@example.test`; privateValues.push(recipient);
        const issued = await context.request.post(root, { headers, data: boardSurface ? { email: recipient, role: 'MEMBER' } : { email: recipient, surface: 'INTERNAL', targetRole: 'MEMBER' } });
        expect(issued.status()).toBe(201); const invitation = (await issued.json()).id; privateValues.push(invitation);
        const path = boardSurface ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`;
        const admission = trackInvitationAdmission(page, org, actor, boardSurface ? board : undefined, path, root);
        await page.goto(path); await expect.poll(admission.ready, { timeout: 30_000 }).toBe(true);
        const revoke = page.getByRole('button', { name: `Revoke invitation for ${recipient}`, exact: true }); await expect(revoke).toBeEnabled();
        const before = writes; const dialog = page.getByRole('dialog', { name: 'Revoke invitation?', exact: true });
        await openAdmittedReview(revoke, dialog); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        await page.keyboard.press('Enter'); await expect(dialog).toHaveCount(0); expect(writes).toBe(before);
        await confirmCurrentReview(revoke, dialog, page.getByRole('button', { name: 'Confirm revocation', exact: true }), () => writes, before + 1);
        await expect(page.getByText('Invitation revocation confirmed.', { exact: true })).toBeVisible();
        await expect(page.getByText('Revoked', { exact: true })).toBeVisible();
        await expect.poll(() => observed(prefix + '_revoke', 'success'), { timeout: 15_000 }).toBe(1);
        expect(observed(prefix + '_revoke', 'use')).toBe(1); expect(observed(prefix + '_revoke', 'failure')).toBe(0);
        expect(observed(prefix + '_revoke', 'open')).toBeGreaterThanOrEqual(2);
        expect(observed(prefix + '_disclosure', 'open')).toBe(1); expect(observed(prefix + '_read', 'success')).toBeGreaterThanOrEqual(1);
        expect(writes).toBe(before + 1);
        const history = await context.request.get(root); expect(history.status()).toBe(200);
        const rows = (await history.json()).items.filter((row: { id: string }) => row.id === invitation); expect(rows).toHaveLength(1);
        expect(Number.isFinite(Date.parse(rows[0].revokedAt))).toBe(true); expect(rows[0].acceptedAt).toBeNull();
      }
      await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true); expect(documents).toBe(2);
      for (const event of events) {
        expect(Object.keys(event).sort()).toEqual(event.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
        if (event.durationMs !== undefined) { expect(Number.isFinite(event.durationMs)).toBe(true); expect(event.durationMs).toBeGreaterThanOrEqual(0); expect(event.durationMs).toBeLessThanOrEqual(60_000); }
      }
      const serialized = JSON.stringify(reports); for (const value of [...privateValues, 'expectedActorId', 'invitationToken', 'targetRole', 'revokedAt']) expect(serialized).not.toContain(value);
    } finally { restore(); }
  });
}
