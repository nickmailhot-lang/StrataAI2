import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

test('PRD-06: desktop list drop feedback precedes persistence and meets its budget', async ({ page, context }) => {
  test.setTimeout(60_000);
  await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `list-feedback-${Date.now()}@example.test`, password: 'list-feedback-correct-horse', displayName: 'List feedback fixture' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const organization = await context.request.post('/organizations', { headers, data: { name: 'List feedback fixture' } });
  expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
  const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'List feedback', visibility: 'PRIVATE' } });
  expect(created.status()).toBe(201); const board = (await created.json()).id;
  const ids: string[] = [];
  for (const name of ['Feedback anchor', 'Feedback moving']) {
    const response = await context.request.post(`/boards/${board}/lists`, { headers, data: { name } });
    expect(response.status()).toBe(201); ids.push((await response.json()).id);
  }
  const baselineResponse = await context.request.get(`/boards/${board}`);
  expect(baselineResponse.status()).toBe(200); const baseline = await baselineResponse.json();
  const reads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
  await page.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
  const handle = page.getByRole('button', { name: 'Drag Feedback moving list', exact: true });
  await expect(handle).toBeEnabled();
  const source = await handle.boundingBox();
  const target = await page.getByRole('region', { name: 'Feedback anchor', exact: true }).boundingBox();
  expect(source).not.toBeNull(); expect(target).not.toBeNull();
  const routePath = `**/lists/${ids[1]}`;
  let releaseWrite!: () => void;
  const writeGate = new Promise<void>(resolve => { releaseWrite = resolve; });
  let writes = 0;
  await page.route(routePath, async route => {
    if (route.request().method() !== 'PATCH') { await route.continue(); return; }
    writes++;
    expect(route.request().headers()['idempotency-key']).toBeTruthy();
    expect(route.request().postDataJSON()).toMatchObject({ name: 'Feedback moving', version: 1, beforeListId: ids[0] });
    await writeGate; await route.continue();
  });
  let feedbackMs: number;
  try {
    // Measure a painted optimistic order while the API request cannot reach
    // PostgreSQL. Use the browser clock explicitly in this serialized callback.
    await page.evaluate(({ moved }) => {
      const state = window as Window & { listFeedback?: Promise<number> };
      state.listFeedback = new Promise<number>(resolve => {
        let started = false;
        const failTimer = window.setTimeout(() => resolve(Infinity), 5000);
        const release = () => {
          if (started) return; started = true;
          const began = window.performance.now();
          const check = () => {
            const first = document.querySelector('[aria-label="Kanban board"] > section');
            const rect = first?.getBoundingClientRect();
            if (first?.getAttribute('aria-labelledby') === `list-name-${moved}` && rect && rect.width > 0 && rect.height > 0
              && rect.left < innerWidth && rect.right > 0 && rect.top < innerHeight && rect.bottom > 0) {
              requestAnimationFrame(() => { clearTimeout(failTimer); resolve(window.performance.now() - began); });
            } else if (window.performance.now() - began >= 2000) resolve(Infinity);
            else requestAnimationFrame(check);
          };
          requestAnimationFrame(check);
        };
        window.addEventListener('pointerup', release, { capture: true, once: true });
        window.addEventListener('mouseup', release, { capture: true, once: true });
      });
    }, { moved: ids[1] });
    await page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2); await page.mouse.down();
    await page.mouse.move(target!.x + target!.width / 2, target!.y + target!.height / 2, { steps: 12 }); await page.mouse.up();
    feedbackMs = await page.evaluate(() => {
      const feedback = (window as Window & { listFeedback?: Promise<number> }).listFeedback;
      if (!feedback) throw new Error('List feedback observer was not installed.');
      return feedback;
    });
    await expect.poll(() => writes).toBe(1);
    // The provisional projection must not have changed persisted ordering.
    const heldResponse = await context.request.get(`/boards/${board}`);
    expect(heldResponse.status()).toBe(200); expect((await heldResponse.json()).lists).toEqual(baseline.lists);
    const acknowledgment = page.waitForResponse(response => response.request().method() === 'PATCH' && new URL(response.url()).pathname === `/lists/${ids[1]}`);
    releaseWrite(); const response = await acknowledgment;
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ id: ids[1], boardId: board, organizationId: org, version: 2 });
    await expect(page.getByRole('button', { name: 'Move Feedback moving list', exact: true })).toBeEnabled();
    const persistedResponse = await context.request.get(`/boards/${board}`);
    expect(persistedResponse.status()).toBe(200); const persisted = await persistedResponse.json();
    expect(persisted.lists.map((column: { list: { id: string } }) => column.list.id)).toEqual([ids[1], ids[0]]);
    expect(persisted.lists[0].list.version).toBe(2); expect(persisted.lists[1]).toEqual(baseline.lists[0]);
    await page.reload(); await expect.poll(reads).toBeGreaterThanOrEqual(4);
    await expect(page.locator('[aria-label="Kanban board"]').getByRole('region').first()).toHaveAccessibleName('Feedback moving');
    expect(writes).toBe(1);
  } finally {
    releaseWrite(); await page.unroute(routePath);
  }
  await test.info().attach('list-feedback-performance.json', { contentType: 'application/json', body: JSON.stringify({
    fixture: { lists: 2, cards: 0, viewport: '1280x844', topology: 'exact release images through Nginx' },
    feedbackObserved: Number.isFinite(feedbackMs), feedbackMs: Number.isFinite(feedbackMs) ? feedbackMs : null,
  }) });
  expect(feedbackMs).toBeLessThan(100);
});
