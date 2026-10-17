import { writeFile } from 'node:fs/promises';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test } from './releaseTest';

const headers = { 'X-StrataAI-Request': '1' };
for (const width of [1280, 390]) {
  test(`PRD-08-VS-01-T-01/ACCOUNT: an original Card creation retry cannot execute as a replacement account at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 900 });
    const member = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let original: Awaited<ReturnType<typeof browser.newContext>> | undefined;
    let pauseFrames = false, holdReads = false;
    let release!: () => void; const reads = new Promise<void>(resolve => { release = resolve; });
    try {
      const ownerCredentials = { email: `card-retry-owner-${crypto.randomUUID()}@example.test`, password: 'card-retry-correct-horse', displayName: 'Replacement Card actor' };
      const memberCredentials = { email: `card-retry-member-${crypto.randomUUID()}@example.test`, password: 'card-retry-correct-horse', displayName: 'Original Card actor' };
      await registerNotificationAccount(context.request, ownerCredentials);
      await registerNotificationAccount(member.request, memberCredentials);
      const replacementActor = (await (await context.request.get('/me')).json()).id as string;
      const originalActor = (await (await member.request.get('/me')).json()).id as string;
      const ownerCookies = await context.cookies();
      const created = await context.request.post('/organizations', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Card command Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { email: memberCredentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' },
      });
      expect(invitation.status()).toBe(201);
      expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      // The original contributor owns this Board; its Organization owner is
      // independently authorized to edit it after the cookie replacement.
      const boardResponse = await member.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { organizationId: org, name: 'Card command Board' } });
      expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id as string;
      const listResponse = await member.request.post(`/boards/${board}/lists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Planning' } });
      expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id as string;
      original = await browser.newContext({ baseURL: test.info().project.use.baseURL, storageState: await member.storageState() });
      await context.addCookies(await member.cookies());
      await page.routeWebSocket(/\/live(?:[/?]|$)/, route => {
        const server = route.connectToServer();
        server.onMessage(payload => { if (!pauseFrames) route.send(payload); });
      });
      await page.route(/\/me\/?(?:\?|$)/, async route => {
        if (holdReads) await reads;
        if (!page.isClosed() && !route.request().failure()) await route.continue();
      });
      await page.goto(`/app/${org}/boards/${board}`);
      await expect(page.getByRole('heading', { name: 'Card command Board', exact: true })).toBeVisible();
      const launcher = page.getByRole('button', { name: 'Add card to Planning', exact: true });
      await expect(launcher).toBeEnabled(); await launcher.focus(); await launcher.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Add card', exact: true });
      await dialog.getByLabel('Card title', { exact: false }).fill('Original account Card');
      const attempts: { key: string | undefined; body: string | null; status: number; id?: string; url: string }[] = [];
      await page.route(url => url.pathname === `/lists/${list}/cards`, async route => {
        if (route.request().method() !== 'POST') return route.continue();
        const result = await route.fetch(); const body = await result.json();
        attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), status: result.status(), id: body.id, url: route.request().url() });
        if (attempts.length === 1) await route.abort('failed');
        else await route.fulfill({ response: result });
      });
      pauseFrames = true;
      const submit = dialog.getByRole('button', { name: 'Create', exact: true });
      await submit.focus(); await submit.press('Enter');
      await expect(dialog.getByRole('alert')).toContainText('Unable to save.');
      expect(attempts).toHaveLength(1); expect(attempts[0].status).toBe(201);
      await expect(submit).toBeEnabled();
      holdReads = true;
      await context.addCookies(ownerCookies);
      expect((await (await context.request.get('/me')).json()).id).toBe(replacementActor);
      await expect(dialog.getByLabel('Card title', { exact: false })).toHaveValue('Original account Card');
      await submit.focus(); await submit.press('Enter');
      await expect.poll(() => attempts.length).toBe(2);
      const canonical = await original.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200);
      const snapshot = await canonical.json(); const column = snapshot.lists.find((x: { list: { id: string } }) => x.list.id === list);
      const evidencePath = test.info().outputPath('card-command-account-evidence.json');
      await writeFile(evidencePath, JSON.stringify({ originalActor, replacementActor, statuses: attempts.map(x => x.status), createdIds: attempts.map(x => x.id), sameKey: attempts[0].key === attempts[1].key, sameBody: attempts[0].body === attempts[1].body, canonicalCardCount: column.cards.length }, null, 2));
      await test.info().attach('card-command-account-evidence', { contentType: 'application/json', path: evidencePath });
      expect(attempts[1].status).toBe(401);
      expect(attempts[1].key).toBe(attempts[0].key); expect(attempts[1].body).toBe(attempts[0].body);
      expect(column.cards).toHaveLength(1);
      expect(column.cards[0]).toMatchObject({ id: attempts[0].id, title: 'Original account Card' });
      await expect(page.getByText('Original account Card', { exact: true })).toHaveCount(0);
      await page.screenshot({ path: test.info().outputPath(`card-account-replacement-refused-${width}.png`), fullPage: true });
    } finally {
      holdReads = false; release(); await original?.close(); await member.close();
    }
  });
}
