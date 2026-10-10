import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-27: bounded intake List continuation preserves reviewed source at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await registerNotificationAccount(context.request, { email: `configuration-intake-pages-${width}-${Date.now()}@example.test`,
      password: 'configuration-pagination-correct-horse', displayName: 'Intake configuration administrator' });
    const headers = { 'X-StrataAI-Request': '1' };
    const created = await context.request.post('/organizations', { headers, data: { name: 'Paged configuration acceptance' } });
    expect(created.status()).toBe(201); const organizationId = (await created.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId, name: 'Paged intake Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const boardId = (await boardReply.json()).id;
    for (let index = 1; index <= 53; index++) {
      const result = await context.request.post(`/boards/${boardId}/lists`, { headers, data: { name: `Paged intake List ${index}` } });
      expect(result.status()).toBe(201);
    }
    const source = `/organizations/${organizationId}/configuration/intake-boards/${boardId}/lists`;
    const firstReply = await context.request.get(source); expect(firstReply.status()).toBe(200);
    const first = await firstReply.json(); expect(first.items).toHaveLength(50);
    expect(first.nextAfterRank).toBe(first.items[49].rank);
    const nextReply = await context.request.get(`${source}?afterRank=${first.nextAfterRank}`); expect(nextReply.status()).toBe(200);
    const next = await nextReply.json(); expect(next.items).toHaveLength(3); expect(next.nextAfterRank).toBeNull();
    expect(new Set([...first.items, ...next.items].map(item => item.id)).size).toBe(53);
    const pinned = first.items[0]; const discarded = first.items[1]; const chosen = next.items[2];
    const restoreWorker = scopedBoardWorker(organizationId);
    try {
      await waitForBoardDelivery(context.request, boardId);
      let fullBoardReads = 0;
      let initialMetadata = false;
      let initialRecheck = false;
      page.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type === 2 && message.item?.organizationId === organizationId
              && Array.isArray(message.item.page?.events)) {
              initialMetadata = true;
              initialRecheck ||= message.item.page.resetRequired || message.item.page.events.length > 0;
            }
          }
        });
      });
      page.on('request', request => {
        if ([`/boards/${boardId}`, `/api/boards/${boardId}`].includes(new URL(request.url()).pathname)) fullBoardReads++;
      });
      await page.goto(`/app/${organizationId}/configuration`);
      await expect.poll(() => initialMetadata).toBe(true);
      if (initialRecheck)
        await expect(page.getByText('Organization authority changed. Checking current configuration before approval.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Load current configuration', exact: true })).toBeEnabled();
      await expect(page.getByText('Private configuration is hidden until current authority is confirmed. Your draft and original submission are preserved.', { exact: true })).not.toBeVisible();
      await page.getByLabel(/^Legal name/).fill('Reviewed paged legal name');
      await page.getByLabel(/^Jurisdiction/).fill('CA-BC');
      await page.getByLabel(/^Organization timezone/).fill('America/Vancouver');
      await page.getByRole('combobox', { name: 'Intake Board', exact: true }).click();
      await page.getByRole('option', { name: 'Paged intake Board', exact: true }).click();
      const more = page.getByRole('button', { name: 'More intake Lists', exact: true }); await expect(more).toBeEnabled();
      await page.getByRole('combobox', { name: 'Intake List', exact: true }).click();
      await page.getByRole('option', { name: pinned.name, exact: true }).click();
      await more.focus(); await page.keyboard.press('Enter'); await expect(more).not.toBeVisible();
      await page.getByRole('combobox', { name: 'Intake List', exact: true }).click();
      await expect(page.getByRole('option', { name: pinned.name, exact: true })).toBeVisible();
      await expect(page.getByRole('option', { name: discarded.name, exact: true })).not.toBeVisible();
      await page.getByRole('option', { name: chosen.name, exact: true }).click();
      const review = page.getByRole('button', { name: 'Review configuration change', exact: true });
      await review.focus(); await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Review configuration change', exact: true });
      await expect(dialog.getByText('Paged intake Board', { exact: true })).toBeVisible();
      await expect(dialog.getByText(chosen.name, { exact: true })).toBeVisible();
      await expect(dialog.getByRole('button', { name: 'Return to draft' })).toBeFocused();
      await page.keyboard.press('Tab'); await expect(dialog.getByRole('button', { name: 'Approve configuration change' })).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(page.getByText('Change acknowledged. Current configuration revision 1.', { exact: true })).toBeVisible();
      await expect(review).toBeFocused();
      const current = await context.request.get(`/organizations/${organizationId}/configuration`); expect(current.status()).toBe(200);
      expect((await current.json()).revision.configuration).toMatchObject({ intakeBoardId: boardId, intakeListId: chosen.id });
      expect(fullBoardReads).toBe(0);
      await page.reload(); await expect(page.getByLabel(/^Legal name/)).toHaveValue('Reviewed paged legal name');
    } finally { restoreWorker(); }
  });
}
