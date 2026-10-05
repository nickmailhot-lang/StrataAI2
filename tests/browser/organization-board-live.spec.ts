import { expect, test, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-04/18: archive directory consumes original Worker events and recovers private scope at ${width}px`, async ({ page, context }) => {
    test.setTimeout(150_000);
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `organization-live-${width}-${Date.now()}@example.test`, password: 'directory-live-correct-horse', displayName: 'Directory administrator' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const profileReply = await context.request.get('/me'); expect(profileReply.status()).toBe(200);
    const profile = await profileReply.json();
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Directory live fixture' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private lifecycle directory', visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201); const board = await created.json();
    const mirror = await context.newPage(); await mirror.setViewportSize({ width, height: 844 });
    let offline = false, socket: WebSocketRoute | undefined;
    const frames = [[], []] as { cursor: string; reset: boolean; types: string[]; versions: number[]; eventIds: string[] }[][];
    function observe(index: number, payload: string) {
        for (const raw of payload.split('\x1e').filter(Boolean)) {
          const message = JSON.parse(raw);
          if (message.type !== 2) continue;
          const item = message.item;
          expect(Object.keys(item).sort()).toEqual(['organizationId', 'page', 'userId']);
          expect(item.organizationId).toBe(org); expect(item.userId).toBe(profile.id);
          expect(item.page.cursor).toMatch(/^[A-Za-z0-9_-]{1,4096}$/);
          for (const event of item.page.events) {
            expect(Object.keys(event).sort()).toEqual(['boardId', 'createdAt', 'eventId', 'eventType', 'version']);
            expect(event.boardId).toBe(board.id); expect(event.eventId).toMatch(/^[0-9a-f-]{36}$/);
          }
          frames[index].push({ cursor: item.page.cursor, reset: item.page.resetRequired,
            types: item.page.events.map((event: { eventType: string }) => event.eventType),
            eventIds: item.page.events.map((event: { eventId: string }) => event.eventId),
            versions: item.page.events.map((event: { version: number }) => event.version) });
        }
    }
    await mirror.routeWebSocket('**/organizations/live*', route => {
      if (offline) { route.close({ code: 1013 }); return; }
      socket = route;
      const server = route.connectToServer();
      // Observe the genuine upstream frame before forwarding it unchanged.
      // No synthetic event or readiness evidence is inserted by this route.
      server.onMessage(payload => { observe(1, payload.toString()); route.send(payload); });
    });
    page.on('websocket', connection => {
      if (!connection.url().includes('/organizations/live')) return;
      connection.on('framereceived', frame => observe(0, frame.payload.toString()));
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board.id);
      for (const client of [page, mirror]) await client.goto(`/app/${org}/archived-boards`);
      await expect.poll(() => frames.every(stream => stream.some(frame => frame.reset))).toBe(true);
      for (const client of [page, mirror]) await expect(client.getByText('No administrable archived Boards on this page.', { exact: true })).toBeVisible();
      const archived = await context.request.post(`/boards/${board.id}/archive`, { headers, data: { version: board.version } });
      expect(archived.status()).toBe(200); const archiveState = await archived.json();
      await expect.poll(() => frames.every(stream => stream.some(frame => frame.types.includes('BOARD_ARCHIVED') && frame.versions.includes(archiveState.version)))).toBe(true);
      for (const client of [page, mirror]) await expect(client.getByRole('article', { name: board.name, exact: true })).toBeVisible();
      await mirror.getByRole('button', { name: `Permanently delete ${board.name} board`, exact: true }).press('Enter');
      await expect(mirror.getByRole('button', { name: 'Confirm permanent deletion', exact: true })).toBeDisabled();
      offline = true; socket!.close({ code: 1013 });
      await expect(mirror.getByRole('dialog')).toHaveCount(0);
      const restored = await context.request.post(`/boards/${board.id}/restore`, { headers, data: { version: archiveState.version } });
      expect(restored.status()).toBe(200); const restoreState = await restored.json();
      await expect.poll(() => frames[0].some(frame => frame.types.includes('BOARD_RESTORED') && frame.versions.includes(restoreState.version))).toBe(true);
      await expect(page.getByRole('article', { name: board.name, exact: true })).toHaveCount(0);
      offline = false;
      await expect.poll(() => frames[1].some(frame => frame.types.includes('BOARD_RESTORED') && frame.versions.includes(restoreState.version)), { timeout: 30_000 }).toBe(true);
      await expect(mirror.getByRole('article', { name: board.name, exact: true })).toHaveCount(0);
      const originalReply = await context.request.get(`/boards/${board.id}/sync`); expect(originalReply.status()).toBe(200);
      const original = await originalReply.json();
      for (const eventType of ['BOARD_ARCHIVED', 'BOARD_RESTORED']) {
        const source = original.events.find((event: { eventType: string }) => event.eventType === eventType);
        expect(source).toBeDefined();
        for (const stream of frames) expect(stream.flatMap(frame => frame.eventIds)).toContain(source.eventId);
      }
      expect((await context.request.post(`/boards/${board.id}/archive`, { headers, data: { version: restoreState.version } })).status()).toBe(200);
      for (const client of [page, mirror]) await expect(client.getByRole('article', { name: board.name, exact: true })).toBeVisible();
      await mirror.getByRole('button', { name: `Permanently delete ${board.name} board`, exact: true }).press('Enter');
      await expect(mirror.getByRole('dialog')).toBeVisible();
      expect((await context.request.post('/auth/logout', { headers, data: {} })).status()).toBe(204);
      for (const client of [page, mirror]) {
        await expect(client.getByRole('article', { name: board.name, exact: true })).toHaveCount(0);
        await expect(client.getByRole('dialog')).toHaveCount(0);
        await expect(client.getByRole('button', { name: 'Confirm permanent deletion', exact: true })).toHaveCount(0);
        expect(await client.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
    } finally { await mirror.close(); restoreWorker(); }
  });
}
