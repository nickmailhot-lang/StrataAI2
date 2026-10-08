import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { trackInvitationAdmission } from './invitationAdmissionTracker';

for (const width of [1280, 390]) for (const boardSurface of [false, true]) {
  test(`PRD-03/05/60: one revocation deadline includes both account checks (Board=${boardSurface}, ${width}px)`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${boardSurface}-${width}-${Date.now()}`;
    const account = { email: `history-deadline-${suffix}@example.test`, password: 'history-deadline-correct-horse', displayName: 'Deadline reviewer' };
    const registered = await context.request.post('/auth/register', { headers, data: account });
    expect(registered.status()).toBe(201); const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Deadline review scope' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    let board: string | undefined;
    if (boardSurface) {
      const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Deadline review Board' } });
      expect(createdBoard.status()).toBe(201); board = (await createdBoard.json()).id;
    }
    const root = board ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
    const path = board ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`;
    const admission = trackInvitationAdmission(page, org, actor, board, path, root);
    const email = `history-deadline-recipient-${suffix}@example.test`;
    const invitation = await context.request.post(root, { headers, data: board ? { email, role: 'MEMBER' } : { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201); const id = (await invitation.json()).id;
    let phase: 'normal' | 'before' | 'after' = 'normal', heldBefore = false, heldAfter = false, writes = 0, documents = 0;
    let releaseBefore!: () => void, releaseAfter!: () => void;
    const beforeGate = new Promise<void>(resolve => { releaseBefore = resolve; });
    const afterGate = new Promise<void>(resolve => { releaseAfter = resolve; });
    await page.route(url => url.pathname === '/me', async route => {
      if (phase === 'before') { heldBefore = true; await beforeGate; }
      else if (phase === 'after') { heldAfter = true; await afterGate; }
      // The deadline aborts the real browser request; late transport completion
      // may be refused by Playwright and cannot restore a private acknowledgment.
      await route.continue().catch(() => {});
    });
    await page.route(url => url.pathname === `${root}/${id}`, async route => {
      if (route.request().method() === 'DELETE') writes++;
      await route.continue();
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    try {
      await page.goto(path);
      // First displayed history can precede the watcher bootstrap invalidation.
      // Wait for the actual scoped head and a protected history read after it.
      await expect.poll(admission.ready).toBe(true);
      const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
      if ((await runtime.json()).mode === 'production')
        await expect(page.getByText('Current invitations checked. Review an invitation again before confirming revocation.', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: `Revoke invitation for ${email}` }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog')).toBeVisible();
      await page.clock.install(); phase = 'before';
      await page.getByRole('button', { name: 'Confirm revocation' }).focus(); await page.keyboard.press('Enter');
      await expect.poll(() => heldBefore).toBe(true);
      await page.clock.runFor(8_000); phase = 'after'; releaseBefore();
      await expect.poll(() => heldAfter).toBe(true); expect(writes).toBe(1);
      await page.clock.runFor(7_001);
      await page.clock.runFor(500); await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Check revocation' })).toBeVisible();
      await expect(page.getByRole('heading', { name: email })).toHaveCount(0);
      await expect(page.getByText('Invitation revocation confirmed.')).toHaveCount(0);
      phase = 'normal'; releaseAfter();
      const committed = await context.request.get(root); expect(committed.status()).toBe(200); const stored = await committed.json();
      expect(stored.items.find((row: { id: string; revokedAt: string | null }) => row.id === id).revokedAt).not.toBeNull();
      await expect(page.getByText('Invitation revocation confirmed.')).toHaveCount(0);
      // A queued live invalidation may already recover through a new admitted
      // history read. Explicitly read current history in either state; the old
      // timed-out command must never be repeated to obtain confirmation.
      await page.getByRole('button', { name: /^(Check revocation|Refresh invitations)$/ }).click();
      await expect(page.getByText('Invitation revocation confirmed.')).toBeVisible();
      const recovered = await context.request.get(root); expect(recovered.status()).toBe(200); expect(await recovered.json()).toEqual(stored);
      expect(writes).toBe(1); expect(documents).toBe(1);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { phase = 'normal'; releaseBefore(); releaseAfter(); }
  });
}
