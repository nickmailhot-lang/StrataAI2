import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test('PRD-13: normal checklist feedback, seek pages and mutation latency meet budgets', async ({ page, context }) => {
  test.setTimeout(180_000); await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `checklist-performance-${Date.now()}@example.test`, password: 'checklist-performance-fixture-credential', displayName: 'Checklist performance fixture' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const organization = await context.request.post('/organizations', { headers, data: { name: 'Checklist performance' } });
  expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
  const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist performance Board', visibility: 'PRIVATE' } });
  expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
  const lists: string[] = [];
  for (let index = 0; index < 3; index++) {
    const response = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `Planning ${index + 1}` } });
    expect(response.status()).toBe(201); lists.push((await response.json()).id);
  }
  let card = '';
  for (let index = 0; index < 50; index++) {
    const response = await context.request.post(`/lists/${lists[index % 3]}/cards`, { headers, data: { title: `Checklist performance Card ${index + 1}` } });
    expect(response.status()).toBe(201); if (!index) card = (await response.json()).id;
  }
  const path = `/cards/${card}/checklists`;
  const createdChecklist = await context.request.post(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Paged preparations', cardVersion: 1 } });
  expect(createdChecklist.status()).toBe(200); const initial = await createdChecklist.json();
  const checklist = initial.checklist.id; let cardVersion = initial.cardVersion, checklistVersion = initial.checklist.version;
  let item = '', itemVersion = 1;
  for (let index = 0; index < 63; index++) {
    const response = await context.request.post(`${path}/${checklist}/items`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {
      text: `Preparation ${index + 1}`, cardVersion, checklistVersion,
    } });
    expect(response.status()).toBe(200); const ack = await response.json();
    expect(ack.cardVersion).toBe(cardVersion + 1); expect(ack.checklist.version).toBe(checklistVersion + 1);
    cardVersion = ack.cardVersion; checklistVersion = ack.checklist.version;
    if (!index) { item = ack.item.id; itemVersion = ack.item.version; }
  }
  const restoreWorker = scopedBoardWorker(org);
  let releaseCreate = () => {};
  try {
    await waitForBoardDelivery(context.request, board);
    await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
    const boardPath = `/app/${org}/boards/${board}`;
    const reads = trackBoardReads(page, board, boardPath); const began = performance.now();
    await page.goto(boardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    await expect(page.getByRole('button', { name: 'Drag Checklist performance Card 1 card', exact: true })).toBeEnabled();
    const usableMs = performance.now() - began;
    const detailBegan = performance.now(); await page.getByRole('link', { name: 'Checklist performance Card 1', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
    const detailMs = performance.now() - detailBegan;
    await page.getByRole('button', { name: 'Show checklists', exact: true }).press('Enter');
    await expect(page.getByText('0 of 63 items complete (0%)', { exact: true })).toBeVisible();
    const itemBegan = performance.now(); await page.getByRole('button', { name: 'Show items in Paged preparations', exact: true }).press('Enter');
    const first = page.getByText(/^Incomplete: Preparation \d+$/);
    await expect(first).toHaveCount(50); const itemPageMs = performance.now() - itemBegan;
    const nextBegan = performance.now(); await page.getByRole('button', { name: 'Next items in Paged preparations', exact: true }).press('Enter');
    await expect(first).toHaveCount(13); const nextItemPageMs = performance.now() - nextBegan;
    await expect(page.getByText('0 of 63 items complete (0%)', { exact: true })).toBeVisible();
    await expect(page.getByText('Incomplete: Preparation 1', { exact: true })).toHaveCount(0);
    await expect(page.getByText('Incomplete: Preparation 63', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Hide checklists', exact: true }).press('Enter');
    const add = page.getByRole('button', { name: 'Add checklist', exact: true }); await expect(add).toBeEnabled(); await add.press('Enter');
    await page.getByRole('textbox', { name: 'New checklist title' }).fill('Feedback measurement');
    const gate = new Promise<void>(resolve => { releaseCreate = resolve; }); let held = false;
    await page.route(`**${path}`, async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      held = true; await gate; await route.continue();
    });
    await page.evaluate(() => {
      const state = window as Window & { checklistFeedback?: Promise<number> };
      state.checklistFeedback = new Promise(resolve => {
        const timer = setTimeout(() => resolve(Infinity), 5000);
        // Measure from the user's actual keyboard/click activation. Admission
        // may replace the form; unrelated submissions must not consume this
        // observer or turn missing feedback into a successful measurement.
        const activate = (event: Event) => {
          if (event instanceof KeyboardEvent && !['Enter', ' '].includes(event.key)) return;
          if (!(event.target instanceof Element)) return;
          const activated = event.target.closest<HTMLButtonElement>('button[type="submit"]');
          if (!activated || activated.disabled || !activated.closest('[aria-label="Create checklist"]')) return;
          document.removeEventListener('keydown', activate, true);
          document.removeEventListener('click', activate, true);
          // This callback executes in the browser. The imported Node clock
          // would be rewritten to a module binding unavailable in that page.
          const began = window.performance.now();
          const frame = () => {
            const region = document.querySelector('[aria-label="Create checklist"]');
            const status = region?.querySelector('[role="status"]');
            const button = region?.querySelector<HTMLButtonElement>('button[type="submit"]');
            if (status?.textContent === 'Creating checklist…' && (!button || button.disabled)) {
              requestAnimationFrame(() => { clearTimeout(timer); resolve(window.performance.now() - began); });
            } else if (window.performance.now() - began >= 2000) { clearTimeout(timer); resolve(Infinity); }
            else requestAnimationFrame(frame);
          }; requestAnimationFrame(frame);
        };
        document.addEventListener('keydown', activate, true);
        document.addEventListener('click', activate, true);
      });
    });
    const create = page.getByRole('button', { name: 'Create checklist', exact: true });
    await expect(create).toBeEnabled(); await create.press('Enter');
    const feedbackMs = await page.evaluate(() => (window as Window & { checklistFeedback: Promise<number> }).checklistFeedback);
    await expect.poll(() => held).toBe(true);
    const created = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === path);
    releaseCreate(); const response = await created; expect(response.status()).toBe(200);
    const ack = await response.json(); expect(ack.cardVersion).toBe(cardVersion + 1); cardVersion = ack.cardVersion;
    await expect(page.getByText('Checklist created.', { exact: true })).toBeVisible(); await page.unroute(`**${path}`);
    const mutations: number[] = [];
    for (let index = 0; index < 20; index++) {
      const began = performance.now(); const response = await context.request.patch(`${path}/${checklist}/items/${item}`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Preparation 1', completed: index % 2 === 0,
          cardVersion, checklistVersion, version: itemVersion },
      });
      const ack = await response.json(); mutations.push(performance.now() - began);
      expect(response.status()).toBe(200); expect(ack.changed).toBe(true); expect(ack.cardVersion).toBe(cardVersion + 1);
      expect(ack.checklist.version).toBe(checklistVersion + 1); expect(ack.item.version).toBe(itemVersion + 1);
      expect(ack.item.completed).toBe(index % 2 === 0);
      cardVersion = ack.cardVersion; checklistVersion = ack.checklist.version; itemVersion = ack.item.version;
    }
    const p95Ms = [...mutations].sort((a, b) => a - b)[18];
    await test.info().attach('checklist-performance.json', { contentType: 'application/json', body: JSON.stringify({
      fixture: { lists: 3, cards: 50, checklists: 2, items: 63, pageSize: 50, samples: 20, viewport: '1280x844', assets: 'warm', topology: 'exact release images through Nginx' },
      usableMs, detailMs, itemPageMs, nextItemPageMs, feedbackObserved: Number.isFinite(feedbackMs), feedbackMs: Number.isFinite(feedbackMs) ? feedbackMs : null,
      mutationP95Ms: p95Ms, mutationSamplesMs: mutations,
    }) });
    expect(usableMs).toBeLessThan(1500); expect(detailMs).toBeLessThan(200); expect(feedbackMs).toBeLessThan(100); expect(p95Ms).toBeLessThan(500);
  } finally { releaseCreate(); restoreWorker(); }
});
