import { expect, test, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-04/18: ordinary directory replays genuine sources and withdraws private MEMBER access at ${width}px`, async ({ browser, context }) => {
    test.setTimeout(150_000);
    const headers = { 'X-StrataAI-Request': '1' };
    const reader = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      const accounts = [context, reader].map((_, index) => ({ email: `ordinary-directory-${width}-${index}-${Date.now()}@example.test`,
        password: 'ordinary-directory-correct-horse', displayName: `Ordinary directory actor ${index}` }));
      for (const [index, client] of [context, reader].entries()) {
        expect((await client.request.post('/auth/register', { headers, data: accounts[index] })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data: accounts[index] })).status()).toBe(200);
      }
      const organization = await context.request.post('/organizations', { headers, data: { name: 'Ordinary directory admission' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: accounts[1].email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await reader.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const me = await reader.request.get('/me'); expect(me.status()).toBe(200); const user = (await me.json()).id;
      const boards: { id: string; name: string; version: number }[] = [];
      for (const name of ['Ordinary admitted private Board', 'Ordinary withheld private Board']) {
        const created = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
        expect(created.status()).toBe(201); boards.push(await created.json());
      }
      expect((await context.request.patch(`/boards/${boards[0].id}/members/${user}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const state = await context.request.get(`/boards/${boards[0].id}`); expect(state.status()).toBe(200);
      const version = (await state.json()).board.version;
      restoreWorker = scopedBoardWorker(org);
      for (const board of boards) await waitForBoardDelivery(context.request, board.id);
      const pages = [await reader.newPage(), await reader.newPage()];
      const frames: { resets: number; ids: string[] }[] = [{ resets: 0, ids: [] }, { resets: 0, ids: [] }];
      for (const [index, page] of pages.entries()) {
        page.on('websocket', socket => {
          if (!socket.url().includes('/organizations/live')) return;
          socket.on('framereceived', frame => {
            for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
              const message = JSON.parse(raw); if (message.type !== 2) continue;
              expect(Object.keys(message.item).sort()).toEqual(['organizationId', 'page', 'userId']);
              expect(message.item.organizationId).toBe(org); expect(message.item.userId).toBe(user);
              if (message.item.page.resetRequired) { frames[index].resets++; expect(message.item.page.events).toEqual([]); }
              for (const event of message.item.page.events) {
                expect(Object.keys(event).sort()).toEqual(['boardId', 'createdAt', 'eventId', 'eventType', 'version']);
                expect(event.boardId).toBe(boards[0].id); frames[index].ids.push(event.eventId);
              }
            }
          });
        });
        await page.goto(`/app/${org}`);
        await expect(page.getByRole('link', { name: boards[0].name, exact: true })).toBeVisible();
        await expect(page.getByText(boards[1].name, { exact: true })).toHaveCount(0);
        await expect.poll(() => frames[index].resets).toBeGreaterThan(0);
      }
      // An actual inaccessible source precedes the admitted lifecycle source.
      expect((await context.request.post(`/boards/${boards[1].id}/archive`, { headers, data: { version: boards[1].version } })).status()).toBe(200);
      const archive = await context.request.post(`/boards/${boards[0].id}/archive`, { headers, data: { version } });
      expect(archive.status()).toBe(200); const archived = await archive.json();
      await waitForBoardDelivery(context.request, boards[0].id);
      const sync = await context.request.get(`/boards/${boards[0].id}/sync`); expect(sync.status()).toBe(200);
      const source = (await sync.json()).events.find((event: { eventType: string }) => event.eventType === 'BOARD_ARCHIVED');
      expect(source).toBeDefined();
      for (const [index, page] of pages.entries()) {
        await expect.poll(() => frames[index].ids.includes(source.eventId)).toBe(true);
        await expect(page.getByRole('link', { name: boards[0].name, exact: true })).toHaveCount(0);
      }
      expect((await context.request.post(`/boards/${boards[0].id}/restore`, { headers, data: { version: archived.version } })).status()).toBe(200);
      for (const page of pages) await expect(page.getByRole('link', { name: boards[0].name, exact: true })).toBeVisible();
      await pages[1].getByRole('button', { name: 'Create board', exact: true }).click();
      await expect(pages[1].getByRole('dialog')).toBeVisible();
      const resetCounts = frames.map(frame => frame.resets);
      expect((await context.request.delete(`/boards/${boards[0].id}/members/${user}`, { headers })).status()).toBe(204);
      for (const [index, page] of pages.entries()) {
        await expect.poll(() => frames[index].resets).toBeGreaterThan(resetCounts[index]);
        await expect(page.getByRole('link', { name: boards[0].name, exact: true })).toHaveCount(0);
        await expect(page.getByRole('dialog')).toHaveCount(0);
        await expect(page.getByText(boards[1].name, { exact: true })).toHaveCount(0);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
      expect((await reader.request.get('/me')).status()).toBe(200);
      const withheld = await reader.request.get(`/boards/${boards[0].id}`); expect(withheld.status()).toBe(404);
      expect(await withheld.text()).not.toContain(boards[0].name);
      expect((await context.request.patch(`/boards/${boards[0].id}/members/${user}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      for (const page of pages) {
        await expect(page.getByRole('link', { name: boards[0].name, exact: true })).toBeVisible();
        await expect(page.getByRole('dialog')).toHaveCount(0);
      }
      const membershipReply = await context.request.get(`/organizations/${org}/members`); expect(membershipReply.status()).toBe(200);
      const membership = (await membershipReply.json()).items.find((item: { userId: string }) => item.userId === user);
      expect(membership).toBeDefined();
      expect((await context.request.delete(`/organizations/${org}/members/${user}?expectedVersion=${membership.version}`, { headers })).status()).toBe(204);
      for (const page of pages) {
        await expect(page.getByRole('link', { name: boards[0].name, exact: true })).toHaveCount(0);
        await expect(page.getByRole('dialog')).toHaveCount(0);
        await expect(page.getByText('Ordinary directory admission', { exact: true })).toHaveCount(0);
      }
      const deniedDirectory = await reader.request.get(`/organizations/${org}/boards`); expect(deniedDirectory.status()).toBe(404);
      expect(await deniedDirectory.text()).not.toContain(boards[0].name);
      expect((await reader.request.get('/me')).status()).toBe(200);
    } finally { await reader.close(); restoreWorker(); }
  });
}

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

test('PRD-04/05: phone archive scope excludes other Boards and withdraws review after Organization membership removal', async ({ context, browser }) => {
  test.setTimeout(150_000);
  const headers = { 'X-StrataAI-Request': '1' };
  const phone = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width: 390, height: 844 } });
  let restoreWorker = () => {};
  try {
    const accounts = [context, phone].map((_, index) => ({ email: `directory-scope-${index}-${Date.now()}@example.test`, password: 'directory-scope-correct-horse', displayName: `Directory actor ${index}` }));
    for (const [index, client] of [context, phone].entries()) {
      expect((await client.request.post('/auth/register', { headers, data: accounts[index] })).status()).toBe(201);
      expect((await client.request.post('/auth/login', { headers, data: accounts[index] })).status()).toBe(200);
    }
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Audience admission fixture' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email: accounts[1].email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await phone.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const me = await phone.request.get('/me'); expect(me.status()).toBe(200); const user = (await me.json()).id;
    const boards: { id: string; name: string; version: number }[] = [];
    for (const name of ['Admitted private archive', 'Withheld private archive']) {
      const created = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(created.status()).toBe(201); boards.push(await created.json());
    }
    expect((await context.request.patch(`/boards/${boards[0].id}/members/${user}`, { headers, data: { role: 'ADMIN' } })).status()).toBe(200);
    const admittedReply = await context.request.get(`/boards/${boards[0].id}`); expect(admittedReply.status()).toBe(200);
    const admittedVersion = (await admittedReply.json()).board.version;
    const hiddenArchive = await context.request.post(`/boards/${boards[1].id}/archive`, { headers, data: { version: boards[1].version } });
    expect(hiddenArchive.status()).toBe(200); const hidden = await hiddenArchive.json();
    restoreWorker = scopedBoardWorker(org);
    for (const board of boards) await waitForBoardDelivery(context.request, board.id);
    const page = await phone.newPage();
    let resets = 0, canonicalArchive = false;
    page.on('websocket', connection => {
      if (!connection.url().includes('/organizations/live')) return;
      connection.on('framereceived', frame => {
        for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
          const message = JSON.parse(raw); if (message.type !== 2) continue;
          expect(message.item.organizationId).toBe(org); expect(message.item.userId).toBe(user);
          if (message.item.page.resetRequired) { resets++; expect(message.item.page.events).toEqual([]); }
          for (const event of message.item.page.events) {
            expect(event.boardId).toBe(boards[0].id);
            if (event.eventType === 'BOARD_ARCHIVED') canonicalArchive = true;
          }
        }
      });
    });
    await page.goto(`/app/${org}/archived-boards`);
    await expect.poll(() => resets).toBeGreaterThan(0);
    await expect(page.getByText('No administrable archived Boards on this page.', { exact: true })).toBeVisible();
    await expect(page.getByText(boards[1].name, { exact: true })).toHaveCount(0);
    // An ineligible source precedes an eligible one. Audience filtering must
    // retain the later real source without leaking the private Board envelope.
    expect((await context.request.post(`/boards/${boards[1].id}/restore`, { headers, data: { version: hidden.version } })).status()).toBe(200);
    const archivedReply = await context.request.post(`/boards/${boards[0].id}/archive`, { headers, data: { version: admittedVersion } });
    expect(archivedReply.status()).toBe(200); const archivedVersion = (await archivedReply.json()).version;
    await expect.poll(() => canonicalArchive).toBe(true);
    await expect(page.getByRole('article', { name: boards[0].name, exact: true })).toBeVisible();
    await expect(page.getByText(boards[1].name, { exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: `Permanently delete ${boards[0].name} board`, exact: true }).press('Enter');
    await expect(page.getByRole('dialog')).toBeVisible();
    const beforeDemotion = resets;
    expect((await context.request.patch(`/boards/${boards[0].id}/members/${user}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
    await expect.poll(() => resets).toBeGreaterThan(beforeDemotion);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('article', { name: boards[0].name, exact: true })).toHaveCount(0);
    await expect(page.getByText('No administrable archived Boards on this page.', { exact: true })).toBeVisible();
    const deniedRestore = await phone.request.post(`/boards/${boards[0].id}/restore`, { headers, data: { version: archivedVersion } });
    expect(deniedRestore.status()).toBe(404); expect(await deniedRestore.text()).not.toContain(boards[0].name);
    const unchanged = await context.request.get(`/boards/${boards[0].id}`); expect(unchanged.status()).toBe(200);
    expect((await unchanged.json()).board).toMatchObject({ version: archivedVersion, lifecycleState: 'archived' });
    const beforeGrant = resets;
    expect((await context.request.patch(`/boards/${boards[0].id}/members/${user}`, { headers, data: { role: 'ADMIN' } })).status()).toBe(200);
    await expect.poll(() => resets).toBeGreaterThan(beforeGrant);
    await expect(page.getByRole('article', { name: boards[0].name, exact: true })).toBeVisible();
    // New permission starts a fresh private snapshot; old destructive consent
    // remains retired even though the same archive is administrable again.
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await page.getByRole('button', { name: `Permanently delete ${boards[0].name} board`, exact: true }).press('Enter');
    await expect(page.getByRole('button', { name: 'Confirm permanent deletion', exact: true })).toBeDisabled();
    const directory = await context.request.get(`/organizations/${org}/members`); expect(directory.status()).toBe(200);
    const member = (await directory.json()).items.find((item: { userId: string }) => item.userId === user); expect(member).toBeDefined();
    expect((await context.request.delete(`/organizations/${org}/members/${user}?expectedVersion=${member.version}`, { headers })).status()).toBe(204);
    await expect(page.getByRole('article', { name: boards[0].name, exact: true })).toHaveCount(0);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    const refused = await phone.request.get(`/organizations/${org}/archived-boards`); expect(refused.status()).toBe(404);
    expect(await refused.text()).not.toContain(boards[0].name);
    expect((await phone.request.get('/me')).status()).toBe(200);
  } finally { await phone.close(); restoreWorker(); }
});
