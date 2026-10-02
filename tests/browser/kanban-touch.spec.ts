import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

test('PRD-06-TC-12: phone touch reorder and boundary-scrolled empty-list move persist', async ({ page, context }) => {
  test.setTimeout(60_000);
  await page.setViewportSize({ width: 390, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `kanban-touch-${Date.now()}@example.test`, password: 'kanban-touch-correct-horse', displayName: 'Touch mover' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const orgResponse = await context.request.post('/organizations', { headers, data: { name: 'Touch fixture' } });
  expect(orgResponse.status()).toBe(201); const org = (await orgResponse.json()).organization.id;
  const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Touch Board', visibility: 'PRIVATE' } });
  expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
  const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Touch list' } });
  expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
  const emptyResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Empty touch destination' } });
  expect(emptyResponse.status()).toBe(201); const empty = (await emptyResponse.json()).id;
  const cards: string[] = [];
  for (const title of ['Touch anchor', 'Touch moving card']) {
    const response = await context.request.post(`/lists/${list}/cards`, { headers, data: { title } });
    expect(response.status()).toBe(201); cards.push((await response.json()).id);
  }
  const baselineResponse = await context.request.get(`/boards/${board}`); expect(baselineResponse.status()).toBe(200);
  const baseline = await baselineResponse.json(); const anchor = baseline.lists[0].cards[0];
  const reads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
  await page.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
  const handle = page.getByRole('button', { name: 'Drag Touch moving card card', exact: true });
  await expect(handle).toBeEnabled();
  await page.getByRole('region', { name: 'Touch list', exact: true }).evaluate(node => node.scrollIntoView({ block: 'center' }));
  const source = await handle.boundingBox();
  const target = await page.getByRole('link', { name: 'Touch anchor', exact: true }).boundingBox();
  expect(source).not.toBeNull(); expect(target).not.toBeNull();
  const touch = await context.newCDPSession(page);
  let touching = false;
  let writes = 0;
  page.on('request', request => { if (request.method() === 'POST' && /\/cards\/[^/]+\/move$/.test(new URL(request.url()).pathname)) writes++; });
  try {
    await touch.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
    const x = source!.x + source!.width / 2; const y = source!.y + source!.height / 2;
    const destinationX = target!.x + target!.width / 2; const destinationY = target!.y + target!.height / 2;
    await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y, id: 1 }] });
    touching = true;
    for (let step = 1; step <= 12; step++) {
      await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: x + (destinationX - x) * step / 12, y: y + (destinationY - y) * step / 12, id: 1 }] });
    }
    await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    touching = false;
    await expect(page.getByText('Move acknowledged. Current placement is being checked.', { exact: true })).toBeVisible();
    await expect(handle).toBeEnabled(); expect(writes).toBe(1);
    const currentResponse = await context.request.get(`/boards/${board}`); expect(currentResponse.status()).toBe(200);
    const current = await currentResponse.json();
    expect(current.lists[0].cards.map((card: { id: string }) => card.id)).toEqual([cards[1], cards[0]]);
    expect(current.lists[0].cards[0].version).toBe(2);
    expect(current.lists[0].cards[1]).toEqual(anchor);
    const reloadReads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
    await page.reload(); await expect.poll(reloadReads).toBeGreaterThanOrEqual(2);
    const links = page.getByRole('region', { name: 'Touch list', exact: true }).getByRole('link');
    await expect(links.first()).toHaveAccessibleName('Touch moving card');
    await expect(handle).toBeEnabled();
    const canvas = page.locator('[aria-label="Kanban board"]');
    await canvas.evaluate(node => node.scrollIntoView({ block: 'center' }));
    const restart = await handle.boundingBox(); const boundary = await canvas.boundingBox();
    expect(restart).not.toBeNull(); expect(boundary).not.toBeNull();
    const startX = restart!.x + restart!.width / 2; const startY = restart!.y + restart!.height / 2;
    const edgeX = boundary!.x + boundary!.width - 10;
    await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: startX, y: startY, id: 1 }] });
    touching = true;
    for (let step = 1; step <= 12; step++) {
      await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: startX + (edgeX - startX) * step / 12, y: startY, id: 1 }] });
    }
    const destination = page.getByText('Drop card at end of Empty touch destination', { exact: true });
    // The trailing list-drop target is another column. Scrolling to the canvas
    // maximum would pass the intended card destination and hide it again.
    await expect.poll(async () => {
      const box = await destination.boundingBox();
      return await canvas.evaluate(node => node.scrollLeft > 0) && Boolean(box && box.x + box.width / 2 > 0 && box.x + box.width / 2 < 390);
    }).toBe(true);
    expect(writes).toBe(1);
    const end = await destination.boundingBox();
    expect(end).not.toBeNull();
    const endX = end!.x + end!.width / 2; const endY = end!.y + end!.height / 2;
    expect(endX).toBeGreaterThan(0); expect(endX).toBeLessThan(390);
    expect(endY).toBeGreaterThan(0); expect(endY).toBeLessThan(844);
    await touch.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: endX, y: endY, id: 1 }] });
    // Sending a CDP event acknowledges dispatch, not React's collision update.
    // Release only after the active drag identifies the intended destination.
    await expect(page.getByText('Touch moving card card can be dropped at the end of Empty touch destination.', { exact: true })).toBeAttached();
    await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    touching = false;
    await expect(page.getByText('Move acknowledged. Current placement is being checked.', { exact: true })).toBeVisible();
    await expect(handle).toBeEnabled(); await expect.poll(() => writes).toBe(2);
    await expect(page.getByRole('region', { name: 'Empty touch destination', exact: true }).getByRole('link', { name: 'Touch moving card', exact: true })).toBeVisible();
    const crossResponse = await context.request.get(`/boards/${board}`); expect(crossResponse.status()).toBe(200);
    const cross = await crossResponse.json();
    expect(cross.lists[0].cards).toEqual([anchor]);
    expect(cross.lists[1].cards).toHaveLength(1);
    expect(cross.lists[1].cards[0]).toMatchObject({ id: cards[1], listId: empty, version: 3 });
    const crossReads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
    await page.reload(); await expect.poll(crossReads).toBeGreaterThanOrEqual(2);
    await expect(page.getByRole('region', { name: 'Empty touch destination', exact: true }).getByRole('link', { name: 'Touch moving card', exact: true })).toBeAttached();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  } finally {
    if (touching) await touch.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
    await touch.detach();
  }
});
