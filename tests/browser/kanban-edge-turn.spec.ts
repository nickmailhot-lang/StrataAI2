import { expect, test } from './releaseTest';
import { focusVisibleBoardCard } from './visibleBoardCard';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const org = id(1), board = id(2), actor = id(3);

// Controlled HTTP isolates native pointer geometry in the actual routed Board.
// Existing kanban-scroll production persistence/cancellation scenarios remain mandatory.
for (const { width, input } of [{ width: 1280, input: 'mouse' }, { width: 390, input: 'mouse' }, { width: 390, input: 'touch' }] as const) {
  test(`UX-003/PRD-06: native ${input} turns from List to Board edge at ${width}px`, async ({ page, context }, info) => {
    await page.setViewportSize({ width, height: 844 });
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    }));
    let writes = 0;
    const summary = { organization: { id: org, name: 'Scrolling Board', description: null, status: 0 }, role: 2 };
    await page.route('**/*', async route => {
      const request = route.request(); if (!['fetch', 'xhr'].includes(request.resourceType())) return route.continue();
      const url = new URL(request.url()), path = url.pathname;
      if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'edge-turn', connectionToken: 'edge-turn', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
      if (!['GET', 'HEAD'].includes(request.method()) && /^\/(boards|cards|lists)(\/|$)/.test(path)) writes++;
      if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
      if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true } });
      if (path === '/navigation/observations') {
        const kind = url.searchParams.get('kind'), card = url.searchParams.get('cardId'), eventId = request.headers()['idempotency-key'];
        return route.fulfill({ json: { eventId, actorId: actor, organizationId: org, boardId: board,
          eventType: kind === 'card' ? 'CARD_OPENED' : 'BOARD_OPENED', entityType: kind === 'card' ? 'Card' : 'Board',
          entityId: card ?? board, version: 1, metadata: {}, createdAt: '2026-10-21T12:00:00Z' } });
      }
      if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === '/organizations/directory') return route.fulfill({ json: { items: [summary], nextCursor: null } });
      if (path === `/organizations/${org}`) return route.fulfill({ json: summary });
      if (path === `/boards/${board}`) return route.fulfill({ json: {
        board: { id: board, organizationId: org, name: 'Boundary scrolling', description: null, version: 1, lifecycleState: 'active', backgroundType: 'COLOR', backgroundValue: null },
        access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
        lists: Array.from({ length: 6 }, (_, n) => ({ list: { id: id(10 + n), name: `Column ${n + 1}`, rank: (n + 1).toString(16).padStart(30, '0'), version: 1, lifecycleState: 'active' },
          cards: n ? [] : Array.from({ length: 12 }, (_, c) => ({ id: id(100 + c), title: `Scrollable card ${c + 1}`, description: null, rank: (c + 1).toString(16).padStart(30, '0'), version: 1 })) })),
      } });
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto(`/app/${org}/boards/${board}`);
    const column = page.getByRole('region', { name: 'Column 1', exact: true }), canvas = page.locator('[aria-label="Kanban board"]');
    const handle = page.getByRole('button', { name: 'Drag Scrollable card 1 card', exact: true });
    await expect(handle).toBeEnabled(); await canvas.evaluate(node => node.scrollIntoView({ block: 'center' }));
    expect(await column.evaluate(node => node.scrollHeight > node.clientHeight)).toBe(true);
    expect(await canvas.evaluate(node => node.scrollWidth > node.clientWidth)).toBe(true);
    const start = await handle.boundingBox(), vertical = await column.boundingBox(), horizontal = await canvas.boundingBox();
    expect(start).not.toBeNull(); expect(vertical).not.toBeNull(); expect(horizontal).not.toBeNull();
    const touch = input === 'touch' ? await context.newCDPSession(page) : undefined;
    let x = start!.x + start!.width / 2, y = start!.y + start!.height / 2, touching = false;
    const move = async (nextX: number, nextY: number) => {
      if (touch) for (let step = 1; step <= 12; step++) await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: x + (nextX - x) * step / 12, y: y + (nextY - y) * step / 12, id: 1 }] });
      else await page.mouse.move(nextX, nextY, { steps: 12 });
      x = nextX; y = nextY;
    };
    try {
      if (touch) {
        await touch.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
        await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y, id: 1 }] }); touching = true;
      } else { await page.mouse.move(x, y); await page.mouse.down(); }
      await move(vertical!.x + vertical!.width / 2, vertical!.y + vertical!.height - 12);
      await expect(handle).toHaveAttribute('aria-pressed', 'true');
      await expect.poll(() => column.evaluate(node => node.scrollTop)).toBeGreaterThan(0);
      await move(horizontal!.x + horizontal!.width - 12, horizontal!.y + horizontal!.height / 2);
      await expect.poll(() => canvas.evaluate(node => node.scrollLeft)).toBeGreaterThan(0);
      await page.screenshot({ path: info.outputPath(`native-edge-turn-${input}-${width}.png`) });
      if (touch) { await touch.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] }); touching = false; }
      else { await page.keyboard.press('Escape'); await page.mouse.up(); }
      await expect(handle).not.toHaveAttribute('aria-pressed', 'true');
    } finally {
      if (touching) await touch!.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
      await touch?.detach();
    }
    expect(writes).toBe(0);
    await canvas.evaluate(node => { node.scrollLeft = 20; }); await column.evaluate(node => { node.scrollTop = 100; });
    const link = page.getByRole('link', { name: 'Scrollable card 4', exact: true }); await focusVisibleBoardCard(link);
    const left = await canvas.evaluate(node => node.scrollLeft), top = await column.evaluate(node => node.scrollTop);
    expect(left).toBeGreaterThan(0); expect(top).toBeGreaterThan(0);
    await link.press('Enter'); await expect(page.getByRole('dialog', { name: 'Card details', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Close', exact: true }).press('Enter'); await expect(link).toBeFocused();
    expect(await canvas.evaluate(node => node.scrollLeft)).toBe(left); expect(await column.evaluate(node => node.scrollTop)).toBe(top);
    expect(writes).toBe(0); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
