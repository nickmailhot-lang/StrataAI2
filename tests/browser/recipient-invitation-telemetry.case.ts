import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { pressObservedRequest } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test.describe.configure({ timeout: 120_000 });
for (const width of [1280, 390]) {
  test(`PRD-02/03/60-TC-01/06/07/11/12: recipient observations preserve original acceptance recovery and private mail proof at ${width}px`, async ({ page, context, browser, baseURL }) => {
    await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; let restore = () => {};
    const suffix = `${width}-${Date.now()}`, recipient = `observed-recipient-${suffix}@example.test`;
    const provider = process.env.STRATAAI_E2E_IDENTITY_PROVIDER_URL ?? 'http://localhost:19090';
    const privateValues: string[] = [recipient];
    async function proof(email: string, path: string) {
      let token = '';
      await expect.poll(async () => {
        const reply = await context.request.get(provider + '/messages'); expect(reply.status()).toBe(200);
        const rows = await reply.json() as { payload: { to: string[]; text: string } }[];
        token = rows.find(row => row.payload.to.includes(email) && row.payload.text.includes(path))?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
        return Boolean(token);
      }, { timeout: 30_000 }).toBe(true);
      privateValues.push(token); return token;
    }
    type Observation = { action: string; kind: string; count: number; durationMs?: number };
    const reports: Observation[] = []; const commands: { path: string; body: string | null }[] = [];
    const count = (action: string, kind: string) => reports.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
    try {
      const health = await context.request.get(provider + '/health'); expect(health.status()).toBe(200);
      const ready = await health.json(); expect(ready.ready).toBe(true);
      if (process.env.STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE) expect(ready.instance).toBe(process.env.STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE);
      for (const [client, email] of [[issuer, `observed-issuer-${suffix}@example.test`], [context, recipient]] as const) {
        privateValues.push(email);
        const account = { email, password: 'recipient-observed-correct-horse', displayName: 'Private observed account' };
        const registration = await client.request.post('/auth/register', { headers, data: account }); expect(registration.status()).toBe(201);
        if (!(await registration.json()).user.emailVerified) expect((await client.request.post('/auth/verify-email', {
          headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { token: await proof(email, '/verify-email#token=') },
        })).status()).toBe(200);
        expect((await client.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      }
      const actor = (await (await context.request.get('/me')).json()).id; privateValues.push(actor);
      const name = 'Private observed recipient council', boardName = 'Private observed recipient Board'; privateValues.push(name, boardName);
      const orgReply = await issuer.request.post('/organizations', { headers, data: { name } }); expect(orgReply.status()).toBe(201);
      const org = (await orgReply.json()).organization.id; privateValues.push(org);
      const boardReply = await issuer.request.post('/boards', { headers, data: { organizationId: org, name: boardName, visibility: 'PRIVATE' } }); expect(boardReply.status()).toBe(201);
      const board = (await boardReply.json()).id; privateValues.push(board);
      restore = scopedBoardWorker(org); await waitForBoardDelivery(issuer.request, board);
      const issued = await issuer.request.post(`/organizations/${org}/invitations`, { headers, data: { email: recipient, surface: 'INTERNAL', targetRole: 'MEMBER' } }); expect(issued.status()).toBe(201);
      const organizationInvitation = await issued.json(); privateValues.push(organizationInvitation.id);
      // Capture this actual mail before issuing the second invitation to the same address.
      await proof(recipient, '/invitation#token=');
      const priorMail = await (await context.request.get(provider + '/messages')).json() as { payload: { text: string } }[];
      const oldTexts = new Set(priorMail.map(row => row.payload.text));
      const boardIssued = await issuer.request.post(`/boards/${board}/invitations`, { headers, data: { email: recipient, role: 'MEMBER' } }); expect(boardIssued.status()).toBe(201);
      const boardInvitation = await boardIssued.json(); privateValues.push(boardInvitation.id);
      let boardToken = '';
      await expect.poll(async () => {
        const rows = await (await context.request.get(provider + '/messages')).json() as { payload: { to: string[]; text: string } }[];
        boardToken = rows.find(row => row.payload.to.includes(recipient) && row.payload.text.includes('/invitation#token=') && !oldTexts.has(row.payload.text))?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
        return Boolean(boardToken);
      }, { timeout: 30_000 }).toBe(true); privateValues.push(boardToken);
      let reads = 0;
      page.on('request', request => { if (new URL(request.url()).pathname === '/me/invitations') reads++; expect(request.url()).not.toContain(boardToken); });
      await page.route(url => url.pathname === '/me/activity-client-events', async route => {
        const body = route.request().postDataJSON() as { events: Observation[] }; reports.push(...body.events);
        const reply = await route.fetch(); expect(reply.status()).toBe(204); await route.fulfill({ response: reply });
      });
      const acceptedPath = `/me/invitations/${organizationInvitation.id}/accept`;
      await page.route(url => url.pathname === acceptedPath || url.pathname === `/me/invitations/${boardInvitation.id}/accept`, async route => {
        const path = new URL(route.request().url()); expect(path.searchParams.get('expectedActorId')).toBe(actor);
        commands.push({ path: path.pathname + path.search, body: route.request().postData() });
        const reply = await route.fetch(); expect(reply.status()).toBe(200);
        if (commands.length === 1) await route.abort('failed'); else await route.fulfill({ response: reply });
      });
      await page.goto('/app/invitations');
      const accept = page.getByRole('button', { name: `Accept invitation to ${name}`, exact: true }); await expect(accept).toBeEnabled();
      const before = reads; await pressObservedRequest(accept, () => commands.length, 1);
      const retry = page.getByRole('button', { name: 'Retry invitation acceptance', exact: true }); await expect(retry).toBeVisible();
      // The actual recipient stream must admit a protected read after the committed change.
      await expect.poll(() => reads, { timeout: 30_000 }).toBeGreaterThan(before); await expect(retry).toBeEnabled();
      await pressObservedRequest(retry, () => commands.length, 2);
      await expect(page.getByText(`Invitation to ${name} accepted.`, { exact: false })).toBeVisible();
      expect(commands).toHaveLength(2); expect(commands[1]).toEqual(commands[0]);
      for (const kind of ['use', 'retry', 'failure', 'success']) await expect.poll(() => count('invitation_recipient_accept', kind), { timeout: 20_000 }).toBe(1);
      expect(count('invitation_recipient_disclosure', 'open')).toBe(1); expect(count('invitation_recipient_read', 'success')).toBeGreaterThan(0);
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(404);
      let reviews = 0;
      await page.route(url => url.pathname === '/invitations/review', async route => {
        expect(route.request().postDataJSON()).toEqual({ token: boardToken }); reviews++;
        const reply = await route.fetch(); expect(reply.status()).toBe(200);
        if (reviews === 1) await route.abort('failed'); else await route.fulfill({ response: reply });
      });
      await page.goto(`/invitation#token=${boardToken}`); await expect(page).toHaveURL(/\/invitation$/);
      const review = page.getByRole('button', { name: 'Review invitation', exact: true });
      await pressObservedRequest(review, () => reviews, 1);
      await expect(page.getByText('The invitation could not be reviewed. Wait and retry.', { exact: true })).toBeVisible();
      await pressObservedRequest(review, () => reviews, 2);
      await expect(page.getByRole('heading', { name: boardName, exact: true })).toBeVisible();
      await pressObservedRequest(page.getByRole('button', { name: 'Accept reviewed invitation', exact: true }), () => commands.length, 3);
      await expect(page.getByText('Invitation acceptance acknowledged.', { exact: false })).toBeVisible();
      await expect.poll(() => count('invitation_link_accept', 'success'), { timeout: 20_000 }).toBe(1);
      expect(count('invitation_link_accept', 'use')).toBe(1); expect(count('invitation_link_disclosure', 'open')).toBe(1);
      for (const kind of ['use', 'retry', 'failure', 'success']) expect(count('invitation_link_review', kind)).toBe(1);
      expect(commands).toHaveLength(3); expect(reviews).toBe(2);
      for (const [root, invitation] of [[`/organizations/${org}/invitations`, organizationInvitation], [`/boards/${board}/invitations`, boardInvitation]] as const) {
        const response = await issuer.request.get(root); expect(response.status()).toBe(200);
        const rows = (await response.json()).items.filter((row: { id: string }) => row.id === invitation.id); expect(rows).toHaveLength(1);
        expect(rows[0].acceptedAt).not.toBeNull(); expect(rows[0].revokedAt).toBeNull(); expect(rows[0].expiresAt).toBe(invitation.expiresAt);
      }
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(200);
      for (const row of reports) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      for (const value of privateValues) expect(JSON.stringify(reports)).not.toContain(value);
      expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain(boardToken);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally {
      const configured = process.env.STRATAAI_INVITATION_EMAIL_ENABLED;
      try { process.env.STRATAAI_INVITATION_EMAIL_ENABLED = 'false'; restore(); }
      finally { if (configured === undefined) delete process.env.STRATAAI_INVITATION_EMAIL_ENABLED; else process.env.STRATAAI_INVITATION_EMAIL_ENABLED = configured; await issuer.close(); }
    }
  });
}
