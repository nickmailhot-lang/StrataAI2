import { writeFile } from 'node:fs/promises';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test } from './releaseTest';

// PRD-07 audit and regression: delay genuine delivery after admission, then
// preserve the original command while a real authorized account is replaced.
const headers = { 'X-StrataAI-Request': '1' };
for (const width of [1280, 390]) {
  test(`PRD-07-VS-01-T-01/ACCOUNT: original List creation retry cannot execute as a replacement account at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 900 });
    const member = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let original: Awaited<ReturnType<typeof browser.newContext>> | undefined;
    let pauseFrames = false, holdReads = false;
    let release!: () => void; const reads = new Promise<void>(resolve => { release = resolve; });
    try {
      const ownerCredentials = { email: `list-retry-owner-${crypto.randomUUID()}@example.test`, password: 'list-retry-correct-horse', displayName: 'Replacement List actor' };
      const memberCredentials = { email: `list-retry-member-${crypto.randomUUID()}@example.test`, password: 'list-retry-correct-horse', displayName: 'Original List actor' };
      await registerNotificationAccount(context.request, ownerCredentials); await registerNotificationAccount(member.request, memberCredentials);
      const replacementActor = (await (await context.request.get('/me')).json()).id as string;
      const originalActor = (await (await member.request.get('/me')).json()).id as string;
      const ownerCookies = await context.cookies();
      const created = await context.request.post('/organizations', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'List command Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { email: memberCredentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201); expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const boardResponse = await member.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { organizationId: org, name: 'List command Board' } });
      expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id as string;
      original = await browser.newContext({ baseURL: test.info().project.use.baseURL, storageState: await member.storageState() });
      await context.addCookies(await member.cookies());
      await page.routeWebSocket(/\/live(?:[/?]|$)/, route => { const server = route.connectToServer(); server.onMessage(payload => { if (!pauseFrames) route.send(payload); }); });
      await page.route(/\/me\/?(?:\?|$)/, async route => { if (holdReads) await reads; if (!page.isClosed() && !route.request().failure()) await route.continue(); });
      await page.goto(`/app/${org}/boards/${board}`);
      await expect(page.getByRole('heading', { name: 'List command Board', exact: true })).toBeVisible();
      const dialog = page.getByRole('dialog', { name: 'Add list', exact: true });
      await expect(async () => {
        if (await dialog.isVisible()) return;
        const launcher = page.getByRole('button', { name: 'Add list', exact: true });
        if (await launcher.isVisible() && await launcher.isEnabled({ timeout: 500 })) {
          await launcher.focus({ timeout: 500 }); await expect(launcher).toBeFocused({ timeout: 500 }); await expect(launcher).toBeEnabled({ timeout: 500 }); await launcher.press('Enter', { timeout: 500 });
        }
        await expect(dialog).toBeVisible({ timeout: 500 });
      }).toPass({ timeout: 5_000 });
      await dialog.getByLabel('List name', { exact: false }).fill('Original account List');
      const attempts: { key: string | undefined; body: string | null; status: number; id?: string; url: string }[] = [];
      await page.route(url => url.pathname === `/boards/${board}/lists`, async route => {
        if (route.request().method() !== 'POST') return route.continue();
        const result = await route.fetch(); const body = await result.json();
        attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), status: result.status(), id: body.id, url: route.request().url() });
        if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
      });
      pauseFrames = true;
      const submit = dialog.getByRole('button', { name: 'Create', exact: true }); await submit.focus(); await submit.press('Enter');
      await expect(dialog.getByRole('alert')).toContainText('Unable to confirm the save');
      expect(attempts).toHaveLength(1); expect(attempts[0].status).toBe(201); await expect(submit).toBeEnabled();
      holdReads = true; await context.addCookies(ownerCookies);
      expect((await (await context.request.get('/me')).json()).id).toBe(replacementActor);
      await expect(dialog.getByLabel('List name', { exact: false })).toHaveValue('Original account List');
      await submit.focus(); await submit.press('Enter'); await expect.poll(() => attempts.length).toBe(2);
      const canonical = await original.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200); const snapshot = await canonical.json();
      const evidencePath = test.info().outputPath('list-command-account-evidence.json');
      await writeFile(evidencePath, JSON.stringify({ originalActor, replacementActor, statuses: attempts.map(x => x.status), createdIds: attempts.map(x => x.id), sameKey: attempts[0].key === attempts[1].key, sameBody: attempts[0].body === attempts[1].body, canonicalListCount: snapshot.lists.length }, null, 2));
      await test.info().attach('list-command-account-evidence', { contentType: 'application/json', path: evidencePath });
      expect(attempts[1].status).toBe(401); expect(attempts[1].key).toBe(attempts[0].key); expect(attempts[1].body).toBe(attempts[0].body);
      expect(snapshot.lists).toHaveLength(1); expect(snapshot.lists[0].list).toMatchObject({ id: attempts[0].id, name: 'Original account List', lifecycleState: 'active' });
      await expect(page.getByLabel('List name', { exact: false })).toHaveCount(0); await expect(page.getByText('List command Board', { exact: true })).toHaveCount(0);
      const refusal = page.getByRole('alert').filter({ hasText: 'Sign in to continue.' }); await expect(refusal).toBeFocused();
      const alertBox = await refusal.boundingBox(), headerBox = await page.getByRole('banner').boundingBox();
      if (!alertBox || !headerBox) throw new Error('Missing refusal/header geometry');
      expect(alertBox.y).toBeGreaterThanOrEqual(headerBox.y + headerBox.height); expect(alertBox.y + alertBox.height).toBeLessThanOrEqual(900);
      await page.screenshot({ path: test.info().outputPath(`list-account-refusal-${width}.png`) });
    } finally { holdReads = false; release(); await original?.close(); await member.close(); }
  });
}
