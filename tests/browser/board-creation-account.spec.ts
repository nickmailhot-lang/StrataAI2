import { writeFile } from 'node:fs/promises';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test } from './releaseTest';

const headers = { 'X-StrataAI-Request': '1' };
for (const width of [1280, 390]) {
  test(`PRD-04-VS-05/AC-05: account replacement cannot turn original Board retry into another actor's creation at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 900 });
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let owner: Awaited<ReturnType<typeof browser.newContext>> | undefined;
    let pauseFrames = false, holdReads = false;
    let release!: () => void; const reads = new Promise<void>(resolve => { release = resolve; });
    try {
      const ownerCredentials = { email: `board-actor-owner-${crypto.randomUUID()}@example.test`, password: 'board-actor-correct-horse', displayName: 'Original Board actor' };
      const memberCredentials = { email: `board-actor-member-${crypto.randomUUID()}@example.test`, password: 'board-actor-correct-horse', displayName: 'Replacement Board actor' };
      await registerNotificationAccount(context.request, ownerCredentials);
      await registerNotificationAccount(recipient.request, memberCredentials);
      const original = await (await context.request.get('/me')).json();
      const replacement = await (await recipient.request.get('/me')).json();
      const created = await context.request.post('/organizations', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Account-bound Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { email: memberCredentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' },
      });
      expect(invitation.status()).toBe(201);
      expect((await recipient.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      owner = await browser.newContext({ baseURL: test.info().project.use.baseURL, storageState: await context.storageState() });
      // Genuine connections/reads are delayed after admission, modelling a
      // stale foreground while the server must enforce command identity.
      await page.routeWebSocket(/\/organizations\/live(?:[/?]|$)/, route => {
        const server = route.connectToServer();
        server.onMessage(payload => { if (!pauseFrames) route.send(payload); });
      });
      await page.route(/\/me\/?(?:\?|$)/, async route => {
        if (holdReads) await reads;
        if (!page.isClosed() && !route.request().failure()) await route.continue();
      });
      await page.goto(`/app/${org}`);
      await expect(page.getByRole('heading', { name: 'Account-bound Organization', exact: true })).toBeVisible();
      await expect(page.getByRole('status')).toHaveText('Current Board access checked.');
      await page.getByRole('button', { name: 'Create board', exact: true }).click();
      const dialog = page.getByRole('dialog');
      await dialog.getByLabel('Name', { exact: false }).fill('Original actor Board');
      await dialog.getByLabel('Description', { exact: true }).fill('Same request, original account');
      const attempts: { key: string | undefined; body: string | null; status: number; id?: string; url: string }[] = [];
      await page.route(url => url.pathname === '/boards', async route => {
        if (route.request().method() !== 'POST') return route.continue();
        const result = await route.fetch(); const body = await result.json();
        attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), status: result.status(), id: body.id, url: route.request().url() });
        if (attempts.length === 1) await route.abort('failed');
        else await route.fulfill({ response: result });
      });
      pauseFrames = true;
      const submit = dialog.getByRole('button', { name: 'Create', exact: true });
      await submit.focus(); await submit.press('Enter');
      await expect(dialog.getByRole('alert')).toContainText('Unable to confirm the save');
      expect(attempts).toHaveLength(1); expect(attempts[0].status).toBe(201);
      await expect(submit).toBeEnabled();
      holdReads = true;
      await context.addCookies(await recipient.cookies());
      expect((await (await context.request.get('/me')).json()).id).toBe(replacement.id);
      await expect(dialog.getByLabel('Name', { exact: false })).toHaveValue('Original actor Board');
      await submit.focus(); await submit.press('Enter');
      await expect.poll(() => attempts.length).toBe(2);
      const directory = await context.request.get(`/organizations/${org}/boards/directory`);
      expect(directory.status()).toBe(200); const remaining = (await directory.json()).items as { id: string }[];
      const evidencePath = test.info().outputPath('original-command-account-evidence.json');
      await writeFile(evidencePath, JSON.stringify({
        originalActor: original.id, replacementActor: replacement.id, statuses: attempts.map(x => x.status),
        createdIds: attempts.map(x => x.id), sameKey: attempts[0].key === attempts[1].key,
        sameBody: attempts[0].body === attempts[1].body, replacementBoardCount: remaining.length,
      }, null, 2));
      await test.info().attach('original-command-account-evidence', { contentType: 'application/json', path: evidencePath });
      expect(attempts[1].status).toBe(401);
      expect(attempts[1].key).toBe(attempts[0].key); expect(attempts[1].body).toBe(attempts[0].body);
      expect(new URL(attempts[1].url).searchParams.get('expectedActorId')).toBe(original.id);
      expect(remaining).toHaveLength(0);
      const canonical = await owner.request.get(`/boards/${attempts[0].id}`); expect(canonical.status()).toBe(200);
      expect((await canonical.json()).board).toMatchObject({ id: attempts[0].id, organizationId: org, name: 'Original actor Board', description: 'Same request, original account', visibility: 'PRIVATE' });
      await expect(page).toHaveURL(/\/login$/);
      await expect(page.getByText('Original actor Board', { exact: true })).toHaveCount(0);
      await page.screenshot({ path: test.info().outputPath(`account-replacement-refused-${width}.png`), fullPage: true });
    } finally {
      holdReads = false; release();
      await owner?.close(); await recipient.close();
    }
  });
}
