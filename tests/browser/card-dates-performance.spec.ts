import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

test('PRD-12: normal dated Board readiness, cached detail and date commands meet budgets', async ({ page, context }) => {
  test.setTimeout(120_000); await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `dates-performance-${Date.now()}@example.test`, password: 'dates-performance-correct-horse',
    displayName: 'Date performance fixture', timezone: 'UTC', locale: 'en-US' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Date performance fixture' } });
  expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
  const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Normal dated Board', visibility: 'PRIVATE' } });
  expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
  const lists: string[] = [];
  for (let index = 0; index < 3; index++) {
    const reply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `Date list ${index + 1}` } });
    expect(reply.status()).toBe(201); lists.push((await reply.json()).id);
  }
  let card = '';
  for (let index = 0; index < 50; index++) {
    const reply = await context.request.post(`/lists/${lists[index % 3]}/cards`, { headers, data: { title: `Date performance card ${index + 1}` } });
    expect(reply.status()).toBe(201); const id = (await reply.json()).id;
    if (!index) card = id;
    const dated = await context.request.patch(`/cards/${id}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { startAt: null, dueAt: '2040-01-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: false, version: 1 } });
    expect(dated.status()).toBe(200); expect(await dated.json()).toMatchObject({ changed: true, card: { id, version: 2 } });
  }
  // Warm only application assets. Include real profile/Board reads and a visible
  // authoritative date badge in usable readiness, rather than timing a loader.
  await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
  const path = `/app/${org}/boards/${board}`, reads = trackBoardReads(page, board, path);
  const began = performance.now();
  await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
  await expect(page.getByRole('button', { name: 'Drag Date performance card 1 card', exact: true })).toBeEnabled();
  const link = page.getByRole('link', { name: 'Date performance card 1', exact: true });
  await expect(link).toHaveAccessibleDescription('Upcoming');
  const usableMs = performance.now() - began;
  const detailBegan = performance.now(); await link.click();
  await expect(page.getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
  const detailMs = performance.now() - detailBegan;
  await expect(page.getByRole('region', { name: 'Card dates', exact: true })).toContainText('Upcoming');
  const samples: number[] = [];
  for (let index = 0; index < 20; index++) {
    const mutationBegan = performance.now();
    const reply = await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { startAt: null, dueAt: '2040-01-03T09:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: index % 2 === 1, version: index + 2 } });
    const result = await reply.json(); samples.push(performance.now() - mutationBegan);
    expect(reply.status()).toBe(200); expect(result).toMatchObject({ changed: true, card: { id: card, version: index + 3 } });
  }
  const p95 = [...samples].sort((a, b) => a - b)[18];
  await test.info().attach('card-dates-performance.json', { contentType: 'application/json', body: JSON.stringify({
    fixture: { lists: 3, cards: 50, datedCards: 50, samples: 20, viewport: '1280x844', assets: 'warm', topology: 'exact release images through Nginx' },
    usableMs, detailMs, mutationP95Ms: p95, mutationSamplesMs: samples,
  }) });
  expect(usableMs).toBeLessThan(1500); expect(detailMs).toBeLessThan(200); expect(p95).toBeLessThan(500);
});
