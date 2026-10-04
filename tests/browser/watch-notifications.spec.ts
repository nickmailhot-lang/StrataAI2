import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';

test('PRD-17-TC-01/07/08/11/12: overlapping native watches deliver one recipient notification and stop after unwatch', async ({ page, context, browser, baseURL }) => {
  test.setTimeout(180_000);
  const headers = { 'X-StrataAI-Request': '1' };
  const peer = await browser.newContext({ baseURL });
  let phone: typeof peer | undefined; let restoreWorker = () => {};
  try {
    const suffix = crypto.randomUUID(); const email = `watch-issuer-${suffix}@example.test`;
    for (const [index, client] of [context, peer].entries()) {
      const account = { email: index ? email : `watch-recipient-${suffix}@example.test`, password: 'watched-notifications-correct-horse', displayName: index ? 'Watch activity issuer' : 'Watch activity recipient' };
      expect((await client.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
      expect((await client.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    }
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Watched notifications' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await peer.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const issuer = (await (await peer.request.get('/me')).json()).id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Overlapping watches', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    expect((await context.request.patch(`/boards/${board}/members/${issuer}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Watched work' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Watched activity source' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
    const path = `/app/${org}/boards/${board}`;
    await page.setViewportSize({ width: 1280, height: 844 });
    await page.goto(path);
    for (const kind of ['Board', 'List', 'Card']) {
      if (kind === 'Card') await page.goto(`${path}/cards/${card}`);
      const open = page.getByRole('button', { name: `${kind} watching`, exact: true });
      await expect(open).toBeEnabled(); await open.press('Enter');
      const dialog = page.getByRole('dialog', { name: `${kind} watching`, exact: true });
      await expect(dialog.getByText(`You are not watching this ${kind}.`, { exact: true })).toBeVisible();
      await dialog.getByRole('button', { name: `Watch ${kind}`, exact: true }).press('Enter');
      await expect(dialog.getByText(`You are watching this ${kind}.`, { exact: true })).toBeVisible();
      await dialog.getByRole('button', { name: 'Done watching', exact: true }).press('Enter');
      await expect(dialog).toHaveCount(0); await expect(open).toBeFocused();
    }
    await waitForBoardDelivery(context.request, board);
    phone = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
    const mobile = await phone.newPage();
    await page.goto(`/app/${org}/notifications`); await mobile.goto(`/app/${org}/notifications`);
    for (const client of [page, mobile]) await expect(client.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    const editor = await peer.newPage(); const reads = trackBoardReads(editor, board, `${path}/cards/${card}`); const revision = trackCardVersion(editor, board, card, `${path}/cards/${card}`);
    await editor.goto(`${path}/cards/${card}`);
    await expect.poll(() => reads(), { timeout: 20_000 }).toBeGreaterThanOrEqual(2);
    await expect.poll(() => revision(), { timeout: 20_000 }).toBe(1);
    const title = editor.getByRole('textbox', { name: 'Card title', exact: true });
    await expect(title).toBeEnabled(); await title.fill('Changed by another authorized user');
    const changed = editor.waitForResponse(reply => new URL(reply.url()).pathname === `/cards/${card}` && reply.request().method() === 'PATCH');
    await editor.getByRole('button', { name: 'Save card', exact: true }).press('Enter');
    const acknowledgment = await changed; expect(acknowledgment.status()).toBe(200); expect((await acknowledgment.json()).version).toBe(2);
    await waitForBoardDelivery(peer.request, board);
    for (const client of [page, mobile]) {
      await expect(client.getByText('Card updated · Unread', { exact: true })).toBeVisible({ timeout: 25_000 });
      await expect(client.getByRole('article')).toHaveCount(1);
      await expect(client.getByRole('link', { name: 'Open Card', exact: true })).toHaveAttribute('href', `${path}/cards/${card}`);
      expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    }
    await mobile.getByRole('button', { name: 'Mark read', exact: true }).press('Enter');
    for (const client of [page, mobile]) await expect(client.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    expect((await context.request.patch(`/cards/${card}`, { headers, data: { title: 'Recipient own action', description: '', version: 2 } })).status()).toBe(200);
    for (const [kind, id] of [['BOARD', board], ['LIST', list], ['CARD', card]]) {
      expect((await context.request.delete(`/watch/${kind}/${id}?version=1`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {} })).status()).toBe(200);
      const state = await context.request.get(`/watch/${kind}/${id}`); expect(state.status()).toBe(200); expect((await state.json()).watching).toBe(false);
    }
    expect((await peer.request.patch(`/cards/${card}`, { headers, data: { title: 'After all watches removed', description: '', version: 3 } })).status()).toBe(200);
    const result = await context.request.get(`/organizations/${org}/notifications`); expect(result.status()).toBe(200);
    const notifications = (await result.json()).items; expect(notifications).toHaveLength(1); expect(notifications[0].actorId).toBe(issuer); expect(notifications[0].type).toBe('CARD_UPDATED'); expect(notifications[0].readAt).not.toBeNull();
    const issuerInbox = await peer.request.get(`/organizations/${org}/notifications`); expect(issuerInbox.status()).toBe(200); expect((await issuerInbox.json()).items).toEqual([]);
  } finally { restoreWorker(); await phone?.close(); await peer.close(); }
});
