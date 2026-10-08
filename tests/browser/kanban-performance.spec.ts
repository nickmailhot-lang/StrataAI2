import { performanceTrace, performanceTraceCondition } from './performanceTracing';
import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { trackBoardReads, waitForBoardReads } from './boardReadTracker';

test.use({ trace: performanceTrace });

for (const width of [1280, 390]) {
test(`PRD-06: normal Board readiness, cached detail and mutation latency meet budgets at ${width}px`, async ({ page, context }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `kanban-performance-${Date.now()}@example.test`, password: 'kanban-performance-correct-horse', displayName: 'Performance fixture' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const orgResponse = await context.request.post('/organizations', { headers, data: { name: 'Performance fixture' } });
  expect(orgResponse.status()).toBe(201); const org = (await orgResponse.json()).organization.id;
  const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Normal performance Board', visibility: 'PRIVATE' } });
  expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
  const lists: string[] = [];
  for (let index = 0; index < 3; index++) {
    const response = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `List ${index + 1}` } });
    expect(response.status()).toBe(201); lists.push((await response.json()).id);
  }
  let card = '';
  let feedbackCard = '';
  for (let index = 0; index < 50; index++) {
    const response = await context.request.post(`/lists/${lists[index % 3]}/cards`, { headers, data: { title: `Performance card ${index + 1}` } });
    expect(response.status()).toBe(201);
    const created = await response.json();
    if (!index) card = created.id;
    if (index === 1) feedbackCard = created.id;
  }
  // Warm application assets, not the Board snapshot or detail route.
  await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
  const reads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
  const started = performance.now();
  await page.goto(`/app/${org}/boards/${board}`); await waitForBoardReads(page, reads, 2);
  expect(reads()).toBeGreaterThanOrEqual(2);
  const handle = page.getByRole('button', { name: 'Drag Performance card 1 card', exact: true }); await expect(handle).toBeEnabled();
  const usableMs = performance.now() - started;
  const baselineResponse = await context.request.get(`/boards/${board}`);
  expect(baselineResponse.status()).toBe(200);
  const baseline = await baselineResponse.json();
  const feedbackAnchor = baseline.lists[0].cards.find((item: { title: string }) => item.title === 'Performance card 4');
  expect(feedbackAnchor).toBeDefined();
  // Keep persistence out of this sample: the next rendered frame must show the
  // destination before this one move request is allowed to reach the server.
  let releaseMove!: () => void;
  const gate = new Promise<void>(resolve => { releaseMove = resolve; });
  let heldMove = false;
  const feedbackRoute = `**/cards/${feedbackCard}/move`;
  await page.route(feedbackRoute, async route => { heldMove = true; await gate; await route.continue(); });
  const touch = width === 390 ? await context.newCDPSession(page) : undefined;
  let touching = false; let pointerX = 0, pointerY = 0;
  async function move(x: number, y: number, steps = 1) {
    if (!touch) { await page.mouse.move(x, y, { steps }); return; }
    const fromX = pointerX, fromY = pointerY;
    for (let step = 1; step <= steps; step++) await touch.send('Input.dispatchTouchEvent', {
      type: 'touchMove', touchPoints: [{ x: fromX + (x - fromX) * step / steps, y: fromY + (y - fromY) * step / steps, id: 1 }],
    });
    pointerX = x; pointerY = y;
  }
  let feedbackMs: number;
  try {
    if (touch) await touch.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 });
    await page.getByRole('button', { name: 'Drag Performance card 2 card', exact: true }).scrollIntoViewIfNeeded();
    if (width === 1280) await page.getByRole('link', { name: 'Performance card 4', exact: true }).scrollIntoViewIfNeeded();
    const source = await page.getByRole('button', { name: 'Drag Performance card 2 card', exact: true }).boundingBox();
    const anchorLink = page.getByRole('link', { name: 'Performance card 4', exact: true });
    expect(source).not.toBeNull();
    await page.evaluate(({ id, destination }) => {
      const state = window as Window & { kanbanFeedback?: Promise<number> };
      state.kanbanFeedback = new Promise<number>(resolve => {
        let started = false;
        const failTimer = window.setTimeout(() => resolve(Infinity), 5000);
        const release = () => {
          if (started) return;
          started = true;
          const began = window.performance.now();
          const check = () => {
            const link = document.querySelector<HTMLAnchorElement>(`a[href$="/cards/${id}"]`);
            const rect = link?.getBoundingClientRect();
            const positioned = link?.closest('section')?.getAttribute('aria-labelledby') === `list-name-${destination}`;
            if (positioned && rect && rect.height > 0 && rect.width > 0 && rect.top < innerHeight && rect.bottom > 0 && rect.left < innerWidth && rect.right > 0) {
              requestAnimationFrame(() => { clearTimeout(failTimer); resolve(window.performance.now() - began); });
            } else if (window.performance.now() - began >= 2000) resolve(Infinity);
            else requestAnimationFrame(check);
          };
          requestAnimationFrame(check);
        };
        window.addEventListener('pointerup', release, { capture: true, once: true });
        window.addEventListener('mouseup', release, { capture: true, once: true });
        window.addEventListener('touchend', release, { capture: true, once: true });
      });
    }, { id: feedbackCard, destination: lists[0] });
    pointerX = source!.x + source!.width / 2; pointerY = source!.y + source!.height / 2;
    if (touch) {
      await touch.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: pointerX, y: pointerY, id: 1 }] }); touching = true;
    } else { await page.mouse.move(pointerX, pointerY); await page.mouse.down(); }
    if (width === 390) {
      // Both columns do not fit on a phone. Retain the same real cross-List
      // movement and use native boundary auto-scroll before the timed drop.
      await move(source!.x + source!.width / 2 + 12, source!.y + source!.height / 2);
      const canvas = page.getByLabel('Kanban board', { exact: true }); const box = await canvas.boundingBox();
      expect(box).not.toBeNull();
      const offset = await canvas.evaluate(node => node.scrollLeft); expect(offset).toBeGreaterThan(0);
      await move(Math.max(0, box!.x) + 10, source!.y + source!.height / 2, 12);
      await expect.poll(async () => {
        const target = await anchorLink.boundingBox();
        return await canvas.evaluate(node => node.scrollLeft) < offset && !!target
          && target.x + target.width / 2 > 0 && target.x + target.width / 2 < width;
      }).toBe(true);
    }
    const target = await anchorLink.boundingBox(); expect(target).not.toBeNull();
    await move(target!.x + target!.width / 2, target!.y + target!.height / 2, 12);
    await expect(page.getByText('Performance card 2 card can be dropped before Performance card 4.', { exact: true })).toBeAttached();
    if (touch) { await touch.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] }); touching = false; }
    else await page.mouse.up();
    feedbackMs = await page.evaluate(() => {
      const feedback = (window as Window & { kanbanFeedback?: Promise<number> }).kanbanFeedback;
      if (!feedback) throw new Error('Kanban feedback observer was not installed.');
      return feedback;
    });
    await expect.poll(() => heldMove).toBe(true);
    const acknowledgment = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === `/cards/${feedbackCard}/move`);
    releaseMove();
    const response = await acknowledgment;
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ id: feedbackCard, listId: lists[0], version: 2 });
    await expect(handle).toBeEnabled();
    const persistedResponse = await context.request.get(`/boards/${board}`);
    expect(persistedResponse.status()).toBe(200);
    const persisted = await persistedResponse.json();
    const destinationCards = persisted.lists[0].cards;
    const movedIndex = destinationCards.findIndex((item: { id: string }) => item.id === feedbackCard);
    expect(movedIndex).toBeGreaterThanOrEqual(0);
    expect(destinationCards[movedIndex + 1]).toEqual(feedbackAnchor);
    expect(persisted.lists[1].cards.some((item: { id: string }) => item.id === feedbackCard)).toBe(false);
  } finally {
    releaseMove();
    try { if (touching) await touch?.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] }); }
    finally { await touch?.detach(); await page.unroute(feedbackRoute); }
  }
  const detailStarted = performance.now();
  await page.getByRole('link', { name: 'Performance card 1', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Card details', exact: true }).getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
  const detailMs = performance.now() - detailStarted;
  const mutations: number[] = [];
  for (let index = 0; index < 20; index++) {
    const began = performance.now();
    const response = await context.request.post(`/cards/${card}/move`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { destinationListId: lists[index % 2 ? 0 : 1], expectedVersion: index + 1 } });
    const body = await response.json(); mutations.push(performance.now() - began);
    expect(response.status()).toBe(200); expect(body.version).toBe(index + 2);
  }
  const p95Ms = [...mutations].sort((a, b) => a - b)[Math.ceil(mutations.length * .95) - 1];
  await test.info().attach('kanban-performance.json', { contentType: 'application/json', body: JSON.stringify({
    fixture: { tracing: performanceTraceCondition, lists: 3, cards: 50, samples: 20, viewport: `${width}x844`, input: width === 390 ? 'chromium-touch' : 'chromium-mouse', assets: 'warm', topology: 'exact release images through Nginx' },
    usableMs, feedbackMs: Number.isFinite(feedbackMs) ? feedbackMs : null, feedbackObserved: Number.isFinite(feedbackMs),
    detailMs, mutationP95Ms: p95Ms, mutationSamplesMs: mutations,
  }) });
  expect(usableMs).toBeLessThan(1500);
  expect(feedbackMs).toBeLessThan(100);
  expect(detailMs).toBeLessThan(200);
  expect(p95Ms).toBeLessThan(500);
});
}
