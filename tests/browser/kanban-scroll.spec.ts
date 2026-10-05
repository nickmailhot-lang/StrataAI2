import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

for (const { width, input } of [{ width: 1280, input: 'mouse' }, { width: 390, input: 'mouse' },
  { width: 390, input: 'touch' }] as const) {
  test(`PRD-06: ${input} drag boundaries scroll Kanban containers and cancellation makes no write at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `kanban-scroll-${width}-${Date.now()}@example.test`, password: 'kanban-scroll-correct-horse', displayName: 'Scroller' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgResponse = await context.request.post('/organizations', { headers, data: { name: 'Scrolling Board' } });
    expect(orgResponse.status()).toBe(201); const org = (await orgResponse.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Boundary scrolling', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    let first = '';
    for (let index = 0; index < 6; index++) {
      const response = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `Column ${index + 1}` } });
      expect(response.status()).toBe(201); if (!index) first = (await response.json()).id;
    }
    for (let index = 0; index < 12; index++) {
      expect((await context.request.post(`/lists/${first}/cards`, { headers, data: { title: `Scrollable card ${index + 1}` } })).status()).toBe(201);
    }
    const baselineResponse = await context.request.get(`/boards/${board}`); expect(baselineResponse.status()).toBe(200);
    const baseline = await baselineResponse.json();
    const reads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
    await page.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    const handle = page.getByRole('button', { name: 'Drag Scrollable card 1 card', exact: true }); await expect(handle).toBeEnabled();
    const column = page.getByRole('region', { name: 'Column 1', exact: true });
    const canvas = page.locator('[aria-label="Kanban board"]');
    await canvas.evaluate(node => node.scrollIntoView({ block: 'center' }));
    expect(await column.evaluate(node => node.scrollHeight > node.clientHeight)).toBe(true);
    expect(await canvas.evaluate(node => node.scrollWidth > node.clientWidth)).toBe(true);
    let writes = 0;
    page.on('request', request => { if (request.method() === 'POST' && /\/cards\/[^/]+\/move$/.test(new URL(request.url()).pathname)) writes++; });
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
    await expect(handle).toBeEnabled();
    const start = await handle.boundingBox(); const vertical = await column.boundingBox(); const horizontal = await canvas.boundingBox();
    expect(start).not.toBeNull(); expect(vertical).not.toBeNull(); expect(horizontal).not.toBeNull();
    const touch = input === 'touch' ? await context.newCDPSession(page) : undefined;
    let x = start!.x + start!.width / 2, y = start!.y + start!.height / 2;
    let touching = false;
    const move = async (nextX: number, nextY: number) => {
      if (touch) {
        for (let step = 1; step <= 12; step++)
          await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{
            x: x + (nextX - x) * step / 12, y: y + (nextY - y) * step / 12, id: 1 }] });
      } else await page.mouse.move(nextX, nextY, { steps: 12 });
      x = nextX; y = nextY;
    };
    try {
      if (touch) {
        await touch.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
        await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y, id: 1 }] });
        touching = true;
      } else { await page.mouse.move(x, y); await page.mouse.down(); }
      await move(vertical!.x + vertical!.width / 2, vertical!.y + vertical!.height - 12);
      await expect(handle).toHaveAttribute('aria-pressed', 'true');
      await expect.poll(() => column.evaluate(node => node.scrollTop)).toBeGreaterThan(0);
      await move(horizontal!.x + horizontal!.width - 12, horizontal!.y + horizontal!.height / 2);
      await expect.poll(() => canvas.evaluate(node => node.scrollLeft)).toBeGreaterThan(0);
      if (touch) {
        await touch.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
        touching = false;
      } else { await page.keyboard.press('Escape'); await page.mouse.up(); }
      await expect(handle).not.toHaveAttribute('aria-pressed', 'true');
    } finally {
      if (touching) await touch!.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
      await touch?.detach();
    }
    expect(writes).toBe(0);
    const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
    expect((await current.json()).lists).toEqual(baseline.lists);
    // Keep a partially scrolled list and canvas while opening card details by keyboard.
    await canvas.evaluate(node => { node.scrollLeft = 20; });
    await column.evaluate(node => { node.scrollTop = 100; });
    const cardLink = page.getByRole('link', { name: 'Scrollable card 4', exact: true });
    await cardLink.evaluate(node => (node as HTMLElement).focus({ preventScroll: true }));
    await expect(cardLink).toBeFocused();
    const canvasLeft = await canvas.evaluate(node => node.scrollLeft);
    const columnTop = await column.evaluate(node => node.scrollTop);
    expect(canvasLeft).toBeGreaterThan(0); expect(columnTop).toBeGreaterThan(0);
    await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog', { name: 'Card details' })).toBeVisible();
    await page.getByRole('button', { name: 'Close', exact: true }).click();
    await expect(cardLink).toBeFocused();
    expect(await canvas.evaluate(node => node.scrollLeft)).toBe(canvasLeft);
    expect(await column.evaluate(node => node.scrollTop)).toBe(columnTop);
    expect(writes).toBe(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
