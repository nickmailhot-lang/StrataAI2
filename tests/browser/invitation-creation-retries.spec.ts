import { expect, test } from './releaseTest';

test('PRD-03/60: invitation creation retries acknowledge one invitation without replaying a bearer token or restoring revoked access', async ({ context, browser }) => {
  const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const headers = { 'X-StrataAI-Request': '1' };
  try {
    const email = `creation-retry-recipient-${Date.now()}@example.test`;
    for (const [index, client] of [context, recipient].entries()) {
      const data = { email: index ? email : `creation-retry-owner-${Date.now()}@example.test`, password: 'browser-invitation-creation-correct-horse', displayName: 'Invitation retry account' };
      expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
      expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
    }
    const created = await context.request.post('/organizations', { headers, data: { name: 'Retry invitation browser' } }); expect(created.status()).toBe(201);
    const org = (await created.json()).organization.id; const key = crypto.randomUUID();
    const data = { email, surface: 'INTERNAL', targetRole: 'ADMIN' }; const keyed = { ...headers, 'Idempotency-Key': key };
    const first = await context.request.post(`/organizations/${org}/invitations`, { headers: keyed, data }); expect(first.status()).toBe(201);
    const ack = await first.json(); expect(ack.invitationToken).toBeNull();
    const replay = await context.request.post(`/organizations/${org}/invitations`, { headers: keyed, data }); expect(replay.status()).toBe(201); expect(await replay.json()).toEqual(ack);
    const changed = await context.request.post(`/organizations/${org}/invitations`, { headers: keyed, data: { ...data, targetRole: 'MEMBER' } }); expect(changed.status()).toBe(409);
    expect((await changed.json()).code).toBe('idempotency_key_reused');
    const pending = await recipient.request.get('/me/invitations'); expect(pending.status()).toBe(200);
    expect((await pending.json()).items.filter((item: { organizationId: string }) => item.organizationId === org)).toHaveLength(1);
    expect((await recipient.request.post(`/me/invitations/${ack.id}/accept`, { headers })).status()).toBe(200);
    const user = (await (await recipient.request.get('/me')).json()).id;
    expect((await context.request.delete(`/organizations/${org}/members/${user}`, { headers })).status()).toBe(204);
    const afterRemoval = await context.request.post(`/organizations/${org}/invitations`, { headers: keyed, data }); expect(afterRemoval.status()).toBe(201); expect(await afterRemoval.json()).toEqual(ack);
    expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(404);
    const pendingAgain = await recipient.request.get('/me/invitations'); expect(pendingAgain.status()).toBe(200);
    expect((await pendingAgain.json()).items.filter((item: { organizationId: string }) => item.organizationId === org)).toHaveLength(0);
  } finally { await recipient.close(); }
});
