import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-06: drag boundaries scroll Kanban containers and cancellation makes no write at ${width}px`, async ({ page, context }) => {
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
    const start = await handle.boundingBox(); const vertical = await column.boundingBox(); const horizontal = await canvas.boundingBox();
    expect(start).not.toBeNull(); expect(vertical).not.toBeNull(); expect(horizontal).not.toBeNull();
    await page.mouse.move(start!.x + start!.width / 2, start!.y + start!.height / 2); await page.mouse.down();
    await page.mouse.move(vertical!.x + vertical!.width / 2, vertical!.y + vertical!.height - 12, { steps: 12 });
    await expect.poll(() => column.evaluate(node => node.scrollTop)).toBeGreaterThan(0);
    await page.mouse.move(horizontal!.x + horizontal!.width - 12, horizontal!.y + horizontal!.height / 2, { steps: 12 });
    await expect.poll(() => canvas.evaluate(node => node.scrollLeft)).toBeGreaterThan(0);
    await page.keyboard.press('Escape'); await page.mouse.up();
    expect(writes).toBe(0);
    const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
    expect((await current.json()).lists).toEqual(baseline.lists);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
