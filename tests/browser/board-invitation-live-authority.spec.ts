import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-03/05/60-TC-05/08/12: Board invitation views withdraw lost administration while read access remains at ${width}px`, async ({ context, browser }) => {
    test.setTimeout(180_000);
    const member = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; let restoreWorker = () => {};
    try {
      const accounts: { id: string; email: string }[] = [];
      for (const [index, client] of [context, member].entries()) {
        const data = { email: `board-invite-live-${width}-${index}-${Date.now()}@example.test`,
          password: 'board-invitation-live-correct-horse', displayName: 'Board invitation live account' };
        const registered = await client.request.post('/auth/register', { headers, data }); expect(registered.status()).toBe(201);
        accounts.push((await registered.json()).user);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const created = await context.request.post('/organizations', { headers, data: { name: 'Board invitation live boundary' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: accounts[1].email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private invitation Board' } });
      expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
      restoreWorker = scopedBoardWorker(org);
      expect((await context.request.patch(`/boards/${board}/members/${accounts[1].id}`, { headers, data: { role: 'ADMIN' } })).status()).toBe(200);
      const recipient = `board-invite-live-recipient-${width}-${Date.now()}@example.test`;
      expect((await context.request.post(`/boards/${board}/invitations`, { headers, data: { email: recipient, role: 'MEMBER' } })).status()).toBe(201);
      const creation = await member.newPage(); const history = await member.newPage();
      let mutations = 0; let documents = 0;
      for (const page of [creation, history]) {
        await page.setViewportSize({ width, height: 844 });
        page.on('request', request => {
          if (request.isNavigationRequest()) documents++;
          if (['POST', 'DELETE'].includes(request.method()) && new URL(request.url()).pathname.startsWith(`/boards/${board}/invitations`)) mutations++;
        });
      }
      await creation.goto(`/app/${org}/boards/${board}/invite`);
      await expect(creation.getByText('Current invitation permissions checked. Review the request before submitting.', { exact: true })).toBeVisible();
      await history.goto(`/app/${org}/boards/${board}/invitations`);
      await expect(history.getByText('Current invitations checked. Review an invitation again before confirming revocation.', { exact: true })).toBeVisible();
      await history.getByRole('button', { name: `Revoke invitation for ${recipient}` }).click();
      await expect(history.getByRole('dialog')).toBeVisible();
      await creation.getByLabel(/^Invitation email/).fill('unsubmitted-private-board-draft@example.test');
      await expect(creation.getByLabel(/^Invitation email/)).toHaveValue('unsubmitted-private-board-draft@example.test');
      expect((await context.request.patch(`/boards/${board}/members/${accounts[1].id}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const readable = await member.request.get(`/boards/${board}`); expect(readable.status()).toBe(200);
      expect((await readable.json()).access.canAdminister).toBe(false);
      await expect(creation.getByText('Board invitations are unavailable to your account.', { exact: true })).toBeVisible({ timeout: 30_000 });
      await expect(history.getByText('Board invitation administration is unavailable.', { exact: true })).toBeVisible({ timeout: 30_000 });
      await expect(creation.getByLabel(/^Invitation email/)).toHaveCount(0);
      await expect(history.getByRole('dialog')).toHaveCount(0);
      await expect(history.getByText(recipient, { exact: true })).toHaveCount(0);
      for (const page of [creation, history]) {
        await expect(page.getByText('Private invitation Board', { exact: true })).toHaveCount(0);
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      }
      expect(mutations).toBe(0); expect(documents).toBe(2);
    } finally { restoreWorker(); await member.close(); }
  });
}
