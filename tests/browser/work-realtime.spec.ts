import { expect, test, type Page } from './releaseTest';
import { execFileSync } from 'node:child_process';

type Probe = { frames: { cursor: string; events: { eventId: string; eventType: string; version: number }[] }[]; closed: boolean; socket: WebSocket };
declare global { interface Window { workProbe: Probe } }

async function subscribe(page: Page, board: string, cursor = '0') {
  await page.evaluate(({ board, cursor }) => {
    const url = new URL('/boards/live', location.href); url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(url);
    window.workProbe = { socket, frames: [], closed: false };
    const heartbeat = setInterval(() => {
      if (socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: 6 }) + '\u001e');
    }, 10_000);
    socket.onopen = () => socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + '\u001e');
    socket.onmessage = event => {
      for (const value of String(event.data).split('\u001e').filter(Boolean)) {
        const message = JSON.parse(value);
        if (Object.keys(message).length === 0)
          socket.send(JSON.stringify({ type: 4, invocationId: 'watch', target: 'Watch', arguments: [board, cursor] }) + '\u001e');
        else if (message.type === 2) window.workProbe.frames.push(message.item);
        else if (message.type === 7) window.workProbe.closed = true;
      }
    };
    socket.onclose = () => { clearInterval(heartbeat); window.workProbe.closed = true; };
  }, { board, cursor });
}

test('PRD-22: exact release WebSocket delivers persisted changes, cursor recovery and logout revocation through Nginx', async ({ page, context, browser }) => {
  test.skip(process.env.CI !== 'true', 'Requires disposable CI PostgreSQL and exact Worker image.');
  test.setTimeout(120_000);
  const headers = { 'X-StrataAI-Request': '1' };
  const email = `live-${Date.now()}@example.test`; const password = 'live-browser-correct-horse';
  expect((await context.request.post('/auth/register', { headers, data: { email, password, displayName: 'Live fixture' } })).ok()).toBeTruthy();
  expect((await context.request.post('/auth/login', { headers, data: { email, password } })).ok()).toBeTruthy();
  const organization = (await (await context.request.post('/organizations', { headers, data: { name: 'Live fixture' } })).json()).organization.id;
  const board = (await (await context.request.post('/boards', { headers, data: { organizationId: organization, name: 'Private live fixture' } })).json()).id;
  const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Live list' } })).json()).id;
  const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Private initial title' } })).json()).id;
  const second = await browser.newContext({ storageState: await context.storageState() });
  const otherPage = await second.newPage();
  const visitor = await browser.newContext(); const visitorPage = await visitor.newPage();
  const files = ['-f', 'compose.release.yml', '-f', 'scripts/ci/compose.identity-test.yml'];
  try {
    await page.goto('/app'); await otherPage.goto('/app'); await visitorPage.goto('/app');
    await subscribe(page, board); await subscribe(otherPage, board); await subscribe(visitorPage, board);
    await expect.poll(() => visitorPage.evaluate(() => window.workProbe.closed)).toBe(true);
    expect(await visitorPage.evaluate(() => window.workProbe.frames)).toHaveLength(0);
    execFileSync('docker', ['compose', ...files, '-f', 'scripts/ci/compose.work-event-test.yml', 'up', '-d', '--force-recreate', '--wait', '--wait-timeout', '180', 'worker'],
      { env: { ...process.env, STRATAAI_TEST_EVENT_ORGANIZATION_ID: organization }, stdio: 'pipe' });
    await expect.poll(() => page.evaluate(() => window.workProbe.frames.some(frame => frame.cursor === '3')), { timeout: 30_000 }).toBe(true);
    expect((await context.request.patch(`/cards/${card}`, { headers, data: { title: 'Private saved title', version: 1 } })).ok()).toBeTruthy();
    for (const target of [page, otherPage])
      await expect.poll(() => target.evaluate(() => window.workProbe.frames.some(frame => frame.events.some(event => event.eventType === 'CARD_UPDATED' && event.version === 2))), { timeout: 15_000 }).toBe(true);
    const cursor = await page.evaluate(() => window.workProbe.frames.at(-1)!.cursor);
    await page.evaluate(() => window.workProbe.socket.close());
    await expect.poll(() => page.evaluate(() => window.workProbe.closed)).toBe(true);
    expect((await context.request.patch(`/cards/${card}`, { headers, data: { title: 'Private disconnected edit', version: 2 } })).ok()).toBeTruthy();
    await expect.poll(() => otherPage.evaluate(() => window.workProbe.frames.some(frame => frame.events.some(event => event.version === 3))), { timeout: 15_000 }).toBe(true);
    await subscribe(page, board, cursor);
    await expect.poll(() => page.evaluate(() => window.workProbe.frames.some(frame => frame.events.some(event => event.version === 3))), { timeout: 15_000 }).toBe(true);
    expect(await page.evaluate(() => JSON.stringify(window.workProbe.frames))).not.toContain('Private disconnected edit');
    const rejected = await context.request.post('/boards/live/negotiate?negotiateVersion=1', { headers: { ...headers, Origin: 'http://evil.example.test' } });
    expect(rejected.status()).toBe(403);
    expect((await context.request.post('/auth/logout', { headers, data: {} })).status()).toBe(204);
    for (const target of [page, otherPage])
      await expect.poll(() => target.evaluate(() => window.workProbe.closed), { timeout: 8_000 }).toBe(true);
  } finally {
    execFileSync('docker', ['compose', ...files, 'up', '-d', '--force-recreate', '--wait', '--wait-timeout', '180', 'worker'], { stdio: 'pipe' });
    await second.close(); await visitor.close();
  }
});
