import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type WebSocketRoute } from './releaseTest';

const organization = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const publisher = '33333333-3333-4333-8333-333333333333';
const root = `/organizations/${organization}/operational-lifecycle`;
const name = 'Admitted lifecycle fixture';
const fact = (version: 2 | 3) => ({ eventId: version === 2 ? '44444444-4444-4444-8444-444444444444' : '55555555-5555-4555-8555-555555555555',
  organizationId: organization, actorId: publisher, entityType: 'Organization', entityId: organization, version,
  eventType: version === 2 ? 'ORGANIZATION_SUSPENDED' : 'ORGANIZATION_REACTIVATED',
  previousStatus: version === 2 ? 'ACTIVE' : 'SUSPENDED', status: version === 2 ? 'SUSPENDED' : 'ACTIVE',
  actingCapacity: 'ORGANIZATION_OWNER', createdAt: '2026-10-22T00:00:00.000Z' });

// Controlled source and real routed consumer: real-service authorization,
// missed-event replay and original command/privacy assertions remain mandatory.
async function sourceFor(page: Page) {
  const source = { version: 1, status: 'ACTIVE', pending: false, denied: false, hold: false, held: 0,
    releases: [] as Array<() => void>, sockets: [] as WebSocketRoute[], cursors: [] as number[],
    facts: [] as ReturnType<typeof fact>[], writes: [] as string[], send: (_events: ReturnType<typeof fact>[], _pending = false) => {} };
  await page.routeWebSocket('**/organizations/live/operational-lifecycle*', socket => {
    source.sockets.push(socket); let invocation = '', cursor = 0;
    source.send = (events, pending = false) => socket.send(JSON.stringify({ type: 2, invocationId: invocation,
      item: { organizationId: organization, userId: actor, page: { organizationId: organization, actorId: actor,
        status: source.status, version: source.version, afterVersion: cursor, nextVersion: null, pending, events } } }) + '\u001e');
    socket.onMessage(message => {
      if (typeof message !== 'string') return;
      for (const part of message.split('\u001e').filter(Boolean)) {
        const value = JSON.parse(part) as { protocol?: string; type?: number; target?: string; invocationId?: string; arguments?: [string, number] };
        if (value.protocol) socket.send('{}\u001e');
        if (value.type === 4 && value.target === 'Watch') {
          invocation = value.invocationId!; cursor = value.arguments![1]; source.cursors.push(cursor);
          source.send(source.facts.filter(event => event.version > cursor), source.pending);
        }
      }
    });
  });
  await page.route('**/*', async route => {
    if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'admission', connectionToken: 'admission',
      availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
    if (path === '/me/activity-client-events') return route.fulfill({ status: 204 });
    if (!['GET', 'HEAD'].includes(route.request().method())) source.writes.push(path);
    if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
    if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' } });
    if (path === root) {
      if (source.hold) { source.held++; await new Promise<void>(resolve => source.releases.push(resolve)); }
      if (route.request().failure()) return;
      return route.fulfill({ status: source.denied ? 403 : 200, json: { organizationId: organization, actorId: actor,
        name, status: source.status, version: source.version, updatedAt: '2026-10-22T00:00:00.000Z', actingCapacity: 'ORGANIZATION_ADMIN' } });
    }
    return route.fulfill({ status: 503, json: {} });
  });
  return source;
}

for (const width of [1280, 768, 390, 320]) for (const admitted of [false, true]) {
  test(`UX-005 lifecycle replay resumes from ${admitted ? 'admitted' : 'unadmitted'} publication at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page);
    const release = () => { source.hold = false; source.releases.splice(0).forEach(resolve => resolve()); };
    try {
      await page.goto(`/app/${organization}/operational-lifecycle`);
      await expect(page.getByRole('button', { name: 'Review lifecycle change', exact: true })).toBeEnabled();
      await expect.poll(() => source.cursors.length).toBe(1);
      const confirmation = page.getByRole('status', { name: 'Live update status', exact: true });
      await expect(confirmation).toHaveCount(0);
      source.version = 2; source.status = 'SUSPENDED'; source.pending = true;
      source.send([], true);
      await expect(page.getByText(/Current lifecycle: Suspended/)).toBeVisible();
      await expect(confirmation).toHaveCount(0);
      source.hold = true; source.pending = false; source.facts = [fact(2)]; source.send(source.facts);
      await expect.poll(() => source.held).toBeGreaterThan(0);
      // Wire arrival and an already visible parent revision are insufficient.
      await expect(page.getByText(/Current lifecycle: Suspended/)).toBeVisible();
      await expect(confirmation).toHaveCount(0);
      if (admitted) {
        release();
        await expect(confirmation).toHaveText('Live updates are current.');
      }
      source.version = 3; source.status = 'ACTIVE'; source.facts = [fact(2), fact(3)];
      await source.sockets[0].close();
      source.hold = false;
      await expect.poll(() => source.cursors.length).toBe(2);
      expect(source.cursors[1]).toBe(admitted ? 2 : 0);
      release();
      await expect(page.getByText(/Current lifecycle: Active/)).toBeVisible();
      await expect(confirmation).toHaveText('Live updates are current.');
      await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      source.denied = true; source.send([]);
      await expect(confirmation).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Review lifecycle change', exact: true })).toHaveCount(0);
      await expect(page.getByText(name, { exact: true })).toHaveCount(0);
      expect(source.writes).toEqual([]);
    } finally { release(); }
  });
}
