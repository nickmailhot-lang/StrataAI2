import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

test('PRD-06: normal Board readiness, cached detail and mutation latency meet budgets', async ({ page, context }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1280, height: 844 });
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
  for (let index = 0; index < 50; index++) {
    const response = await context.request.post(`/lists/${lists[index % 3]}/cards`, { headers, data: { title: `Performance card ${index + 1}` } });
    expect(response.status()).toBe(201); if (!index) card = (await response.json()).id;
  }
  // Warm application assets, not the Board snapshot or detail route.
  await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Organizations', exact: true })).toBeVisible();
  const reads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
  const started = performance.now();
  await page.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
  const handle = page.getByRole('button', { name: 'Drag Performance card 1 card', exact: true }); await expect(handle).toBeEnabled();
  const usableMs = performance.now() - started;
  const detailStarted = performance.now();
  await page.getByRole('link', { name: 'Performance card 1', exact: true }).click();
  await expect(page.getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
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
    fixture: { lists: 3, cards: 50, samples: 20, viewport: '1280x844', assets: 'warm', topology: 'exact release images through Nginx' },
    usableMs, detailMs, mutationP95Ms: p95Ms, mutationSamplesMs: mutations,
  }) });
  expect(usableMs).toBeLessThan(1500);
  expect(detailMs).toBeLessThan(200);
  expect(p95Ms).toBeLessThan(500);
});
