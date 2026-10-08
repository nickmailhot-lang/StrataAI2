import { expect, test } from './releaseTest';
import { focusAdmittedControl, pressAdmittedAction } from './keyboardAdmission';
import { trackInvitationAdmission } from './invitationAdmissionTracker';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03/60: keyboard invitation creation survives reload and separates Portal grants at ${viewport.width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000);
    await page.setViewportSize(viewport);
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const preferences = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    try {
      const email = `invitation-ui-recipient-${viewport.width}-${Date.now()}@example.test`;
      const ownerEmail = `invitation-ui-owner-${viewport.width}-${Date.now()}@example.test`;
      for (const [index, client] of [context, recipient].entries()) {
        const data = { email: index ? email : ownerEmail, password: 'browser-invitation-ui-correct-horse', displayName: 'Invitation administrator', locale: 'en-US', timezone: 'Pacific/Honolulu' };
        expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      expect((await preferences.request.post('/auth/login', { headers, data: { email: ownerEmail, password: 'browser-invitation-ui-correct-horse' } })).status()).toBe(200);
      const created = await context.request.post('/organizations', { headers, data: { name: 'Keyboard invitation administration' } }); expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id;
      const addedActors: string[] = []; let admissions = 0;
      page.on('request', request => {
        if (request.method() === 'GET' && new URL(request.url()).pathname.startsWith(`/organizations/${org}/members/`)) admissions++;
      });
      page.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type === 2 && message.item?.organizationId === org)
              for (const event of message.item.page.events)
                if (event.eventType === 'ORGANIZATION_MEMBER_ADDED') addedActors.push(event.actorId);
          }
        });
      });
      const reviewedActor = (await (await context.request.get('/me')).json()).id;
      const creation = trackInvitationAdmission(page, org, reviewedActor, undefined, `/app/${org}/invite`);
      await page.goto(`/app/${org}/members`);
      await pressAdmittedAction(page.getByRole('link', { name: 'Create Organization invitation' }));
      await expect(page.getByText('Current invitation permissions checked. Review the request before submitting.', { exact: true })).toBeVisible();
      await expect.poll(creation.ready, { timeout: 30_000 }).toBe(true);
      await page.getByLabel(/^Invitation email/).fill(email);
      const roleChoice = page.getByRole('combobox', { name: 'Invitation role' }); await focusAdmittedControl(roleChoice); await roleChoice.press('ArrowDown');
      await pressAdmittedAction(page.getByRole('option', { name: 'Admin', exact: true }));
      const writes: { key: string | undefined; input: unknown }[] = [];
      await page.route(url => url.pathname === `/organizations/${org}/invitations`, async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(reviewedActor);
        writes.push({ key: route.request().headers()['idempotency-key'], input: route.request().postDataJSON() });
        if (writes.length === 1) {
          expect((await route.fetch()).status()).toBe(201);
          const profile = await (await preferences.request.get('/me')).json();
          expect((await preferences.request.patch('/me', { headers, data: { timezone: 'Asia/Tokyo', version: profile.version } })).status()).toBe(200);
          await route.abort('timedout');
        }
        else await route.continue();
      });
      await pressAdmittedAction(page.getByRole('button', { name: 'Create invitation', exact: true }));
      await expect(page.getByText(/invitation could not be confirmed|prior invitation request is awaiting acknowledgment/)).toBeVisible();
      expect(writes).toHaveLength(1); await expect(page.getByLabel(/^Invitation email/)).toBeDisabled();
      const beforeReload = creation.heads();
      await page.reload(); await expect(page.getByText(/prior invitation request is awaiting acknowledgment/)).toBeVisible();
      await expect(page.getByText('Current invitation permissions checked. Review the request before submitting.', { exact: true })).toBeVisible();
      await expect.poll(() => creation.heads() > beforeReload && creation.ready(), { timeout: 30_000 }).toBe(true);
      await expect(page.getByLabel(/^Invitation email/)).toHaveValue(email);
      await pressAdmittedAction(page.getByRole('button', { name: 'Retry same invitation' }));
      await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible(); await expect(page.getByText(/Email delivery is not confirmed here/)).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[0]).toEqual(writes[1]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(writes[0].input).toEqual({ email, surface: 'INTERNAL', targetRole: 'ADMIN' });
      const createdHistory = (await (await context.request.get(`/organizations/${org}/invitations`)).json()).items;
      expect(createdHistory).toHaveLength(1);
      const expiryCaption = await page.evaluate(instant => `Expires: ${new Intl.DateTimeFormat('en-US', {
        timeZone: 'Asia/Tokyo', year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
      }).format(new Date(instant))}`, createdHistory[0].expiresAt);
      await expect(page.getByText(expiryCaption, { exact: true })).toBeVisible();
      await focusAdmittedControl(page.getByRole('button', { name: 'Create another invitation', exact: true }));
      const afterRetryProfile = await (await preferences.request.get('/me')).json();
      expect((await preferences.request.patch('/me', { headers, data: { timezone: 'UTC', version: afterRetryProfile.version } })).status()).toBe(200);
      const recoveredCaption = await page.evaluate(instant => `Expires: ${new Intl.DateTimeFormat('en-US', {
        timeZone: 'UTC', year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
      }).format(new Date(instant))}`, createdHistory[0].expiresAt);
      await expect(page.getByText(recoveredCaption, { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('button', { name: 'Create another invitation', exact: true })).toBeFocused();
      expect((await (await context.request.get(`/organizations/${org}/invitations`)).json()).items).toEqual(createdHistory);
      expect(writes).toHaveLength(2);
      const pending = await recipient.request.get('/me/invitations'); expect(pending.status()).toBe(200);
      const invitations = (await pending.json()).items.filter((item: { organizationId: string }) => item.organizationId === org); expect(invitations).toHaveLength(1);
      const beforeAdmission = admissions;
      expect((await recipient.request.post(`/me/invitations/${invitations[0].id}/accept`, { headers })).status()).toBe(200);
      const recipientId = (await (await recipient.request.get('/me')).json()).id;
      const membership = await recipient.request.get(`/organizations/${org}/members/${recipientId}`); expect(membership.status()).toBe(200);
      const before = (await membership.json()).member;
      await expect.poll(() => addedActors.includes(recipientId), { timeout: 30_000 }).toBe(true);
      await expect.poll(() => admissions, { timeout: 30_000 }).toBeGreaterThan(beforeAdmission);
      await expect(page.getByText('Invitation creation acknowledged.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Retry same invitation' })).toBeDisabled();
      expect(writes).toHaveLength(2);
      await pressAdmittedAction(page.getByRole('button', { name: 'Create another invitation' }));
      await page.getByLabel(/^Invitation email/).fill(email);
      const choice = page.getByRole('combobox', { name: 'Access surface' }); await focusAdmittedControl(choice); await choice.press('ArrowDown');
      await pressAdmittedAction(page.getByRole('option', { name: 'Owner Portal', exact: true }));
      await expect(page.getByRole('combobox', { name: 'Invitation role' })).toHaveText('Owner');
      await pressAdmittedAction(page.getByRole('button', { name: 'Create invitation', exact: true }));
      await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible(); expect(writes).toHaveLength(3);
      expect(writes[2].key).not.toBe(writes[0].key); expect(writes[2].input).toEqual({ email, surface: 'PORTAL', targetRole: 'OWNER' });
      const portalPending = await recipient.request.get('/me/invitations'); expect(portalPending.status()).toBe(200);
      const portalInvite = (await portalPending.json()).items.find((item: { organizationId: string; surface: string }) => item.organizationId === org && item.surface === 'PORTAL'); expect(portalInvite).toBeDefined();
      expect((await recipient.request.post(`/me/invitations/${portalInvite.id}/accept`, { headers })).status()).toBe(200);
      const after = await recipient.request.get(`/organizations/${org}/members/${recipientId}`); expect(after.status()).toBe(200);
      expect((await after.json()).member).toEqual(before);
    } finally { await preferences.close(); await recipient.close(); }
  });
}
