import { expect, test } from './releaseTest';

test('PRD-03-TC-04/05: Organization directory admission follows current internal membership and separates Portal access', async ({ context, browser }) => {
  const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const portal = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const headers = { 'X-StrataAI-Request': '1' };
  try {
    const ids: string[] = [];
    for (const [index, client] of [context, recipient, portal].entries()) {
      const data = { email: `directory-browser-${index}-${Date.now()}@example.test`, password: 'browser-directory-correct-horse', displayName: `Directory browser ${index}` };
      const registered = await client.request.post('/auth/register', { headers, data }); expect(registered.status()).toBe(201);
      ids.push((await registered.json()).user.id);
      expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
    }
    const created = await context.request.post('/organizations', { headers, data: { name: 'Browser directory organization' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(404);
    for (const [client, surface, targetRole] of [[recipient, 'INTERNAL', 'ADMIN'], [portal, 'PORTAL', 'OWNER']] as const) {
      const email = (await (await client.request.get('/me')).json()).email;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface, targetRole } });
      expect(invitation.status()).toBe(201);
      expect((await client.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    }
    const directory = await recipient.request.get(`/organizations/${org}/members`); expect(directory.status()).toBe(200);
    const page = await directory.json(); expect(page.organizationId).toBe(org); expect(page.nextCursor).toBeNull();
    expect(page.items.map((item: { userId: string }) => item.userId).sort()).toEqual(ids.slice(0, 2).sort());
    expect(page.items.filter((item: { isUsableOwner: boolean }) => item.isUsableOwner)).toHaveLength(1);
    expect((await portal.request.get(`/organizations/${org}/members`)).status()).toBe(404);
    expect((await recipient.request.delete(`/organizations/${org}/members/${ids[0]}`, { headers })).status()).toBe(403);
    const target = page.items.find((item: { userId: string }) => item.userId === ids[1]);
    const stale = await context.request.delete(`/organizations/${org}/members/${ids[1]}?expectedVersion=${target.version + 1}`, { headers });
    expect(stale.status()).toBe(409); expect((await stale.json()).code).toBe('member_version_conflict');
    expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(200);
    expect((await context.request.delete(`/organizations/${org}/members/${ids[1]}?expectedVersion=${target.version}`, { headers })).status()).toBe(204);
    const revoked = await recipient.request.get(`/organizations/${org}/members`); expect(revoked.status()).toBe(404);
    expect(await revoked.text()).not.toContain('Directory browser');
    expect((await context.request.get(`/organizations/${org}/members?after=not-a-cursor`)).status()).toBe(400);
  } finally { await recipient.close(); await portal.close(); }
});
