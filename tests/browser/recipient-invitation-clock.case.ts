import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test.describe.configure({ timeout: 120_000 });

for (const width of [1280, 390]) {
  test(`PRD-02/03/60-TC-03/06/11/12: recipient discovery and actual proof review reject malformed expiry before consent at ${width}px`, async ({ page, context, browser, baseURL }) => {
    await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL }); const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${width}-${Date.now()}`; let restore = () => {};
    try {
      for (const [client, kind] of [[issuer, 'issuer'], [context, 'recipient']] as const) {
        const account = { email: `clock-${kind}-${suffix}@example.test`, password: 'recipient-clock-correct-horse', displayName: `Clock ${kind}` };
        const registration = await client.request.post('/auth/register', { headers, data: account }); expect(registration.status()).toBe(201);
        if (!(await registration.json()).user.emailVerified) {
          let verification = '';
          await expect.poll(async () => {
            const reply = await client.request.get((process.env.STRATAAI_E2E_IDENTITY_PROVIDER_URL ?? 'http://localhost:19090') + '/messages');
            expect(reply.status()).toBe(200);
            const messages = await reply.json() as { payload: { to: string[]; text: string } }[];
            verification = messages.find(row => row.payload.to.includes(account.email) && row.payload.text.includes('/verify-email#token='))?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
            return Boolean(verification);
          }, { timeout: 30_000 }).toBe(true);
          expect((await client.request.post('/auth/verify-email', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { token: verification } })).status()).toBe(200);
        }
        expect((await client.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      }
      const recipientEmail = `clock-recipient-${suffix}@example.test`;
      const actor = (await (await context.request.get('/me')).json()).id;
      const orgName = 'Private recipient clock council';
      const createdOrg = await issuer.request.post('/organizations', { headers, data: { name: orgName } });
      expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
      const createdBoard = await issuer.request.post('/boards', { headers, data: { organizationId: org, name: 'Private clock Board', visibility: 'PRIVATE' } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
      restore = scopedBoardWorker(org); await waitForBoardDelivery(issuer.request, board);
      const issued = await issuer.request.post(`/organizations/${org}/invitations`, { headers, data: { email: recipientEmail, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(issued.status()).toBe(201); const issuedBody = await issued.json(); const invitation = issuedBody.id;
      const provider = process.env.STRATAAI_E2E_IDENTITY_PROVIDER_URL ?? 'http://localhost:19090';
      const health = await context.request.get(provider + '/health'); expect(health.status()).toBe(200); const providerHealth = await health.json(); expect(providerHealth.ready).toBe(true);
      if (process.env.STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE) expect(providerHealth.instance).toBe(process.env.STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE);
      let token = '';
      await expect.poll(async () => {
        const reply = await context.request.get(provider + '/messages'); expect(reply.status()).toBe(200);
        const messages = await reply.json() as { payload: { to: string[]; text: string } }[];
        const message = messages.find(row => row.payload.to.includes(recipientEmail) && row.payload.text.includes('/invitation#token='));
        token = message?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? ''; return Boolean(token);
      }, { timeout: 30_000 }).toBe(true);
      let malformed = true; let reads = 0; let acceptances = 0; const urls: string[] = [];
      page.on('request', request => {
        urls.push(request.url()); if (request.method() === 'POST' && new URL(request.url()).pathname === `/me/invitations/${invitation}/accept`) acceptances++;
      });
      const discovery = (url: URL) => url.pathname === '/me/invitations';
      await page.route(discovery, async route => {
        const reply = await route.fetch(); expect(reply.status()).toBe(200); const body = await reply.json(); reads++;
        if (malformed) body.items = body.items.map((row: { id: string }) => row.id === invitation ? { ...row, expiresAt: '2035-02-30T12:00:00Z' } : row);
        await route.fulfill({ response: reply, json: body });
      });
      await page.goto('/app/invitations');
      await expect(page.getByText('Unable to load invitations. Please refresh and try again.', { exact: true })).toBeVisible();
      expect(reads).toBeGreaterThan(0); await expect(page.getByRole('heading', { name: orgName, exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: /^Accept invitation to/ })).toHaveCount(0); expect(acceptances).toBe(0);
      malformed = false;
      await openAdmittedReview(page.getByRole('button', { name: 'Refresh invitations', exact: true }), page.getByRole('heading', { name: orgName, exact: true }));
      await expect(page.getByRole('button', { name: `Accept invitation to ${orgName}`, exact: true })).toBeEnabled();
      await page.unroute(discovery);
      let badProof = true; const proofs: string[] = [];
      await page.route(url => url.pathname === '/invitations/review', async route => {
        proofs.push(route.request().postData()!); expect(route.request().postDataJSON()).toEqual({ token });
        expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(actor);
        const reply = await route.fetch(); expect(reply.status()).toBe(200); const body = await reply.json(); expect(body.id).toBe(invitation);
        if (badProof) body.expiresAt = '2035-03-08T24:00:00Z';
        await route.fulfill({ response: reply, json: body });
      });
      await page.goto(`/invitation#token=${token}`); await expect(page).toHaveURL(/\/invitation$/);
      const review = page.getByRole('button', { name: 'Review invitation', exact: true });
      await pressObservedRequest(review, () => proofs.length, 1);
      await expect(page.getByText('The invitation could not be reviewed. Wait and retry.', { exact: true })).toBeVisible();
      await expect(page.getByRole('heading', { name: orgName, exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Accept reviewed invitation', exact: true })).toHaveCount(0); expect(acceptances).toBe(0);
      badProof = false; await pressObservedRequest(review, () => proofs.length, 2);
      await expect(page.getByRole('heading', { name: orgName, exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Accept reviewed invitation', exact: true })).toBeEnabled();
      expect(proofs).toHaveLength(2); expect(proofs[1]).toBe(proofs[0]); expect(acceptances).toBe(0);
      expect(urls.every(url => !url.includes(token))).toBe(true);
      expect(await page.evaluate(() => JSON.stringify([localStorage, sessionStorage]))).not.toContain(token);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(404);
      const history = await issuer.request.get(`/organizations/${org}/invitations`); expect(history.status()).toBe(200);
      const row = (await history.json()).items.find((item: { id: string }) => item.id === invitation);
      expect(row).toMatchObject({ acceptedAt: null, revokedAt: null }); expect(row.expiresAt).toBe(issuedBody.expiresAt);
    } finally {
      // The global identity-mail Worker is unscoped between cases. Invitation
      // delivery runs only while the original helper owns this actual tenant.
      const configuredMail = process.env.STRATAAI_INVITATION_EMAIL_ENABLED;
      try { process.env.STRATAAI_INVITATION_EMAIL_ENABLED = 'false'; restore(); }
      finally {
        if (configuredMail === undefined) delete process.env.STRATAAI_INVITATION_EMAIL_ENABLED;
        else process.env.STRATAAI_INVITATION_EMAIL_ENABLED = configuredMail;
        await issuer.close();
      }
    }
  });
}
