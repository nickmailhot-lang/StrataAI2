import { expect, test } from './releaseTest';

test('PRD-03-TC-01/04/11/12/13: bounded Organization pages and direct deep links on desktop and phone', async ({ page, context }) => {
  test.setTimeout(120_000); // Includes 51 real acknowledged fixture writes before UI verification.
  const headers = { 'X-StrataAI-Request': '1' };
  const credentials = { email: `paged-organizations-${Date.now()}@example.test`, password: 'browser-paged-organizations-correct-horse', displayName: 'Directory owner' };
  expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
  for (let index = 0; index < 51; index++) {
    const created = await context.request.post('/organizations', { headers, data: { name: `Paged council ${index}` } });
    expect(created.status()).toBe(201);
  }
  const initial = await context.request.get('/organizations/directory'); expect(initial.status()).toBe(200);
  const first = await initial.json(); expect(first.items).toHaveLength(50); expect(first.nextCursor).toBeTruthy();
  const continued = await context.request.get(`/organizations/directory?after=${first.nextCursor}`); expect(continued.status()).toBe(200);
  const tail = await continued.json(); expect(tail.items).toHaveLength(1); expect(tail.nextCursor).toBeNull();
  const later = tail.items[0].organization; const early = first.items[0].organization;
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 844 });
    await page.goto('/app');
    await expect(page.getByRole('link', { name: early.name, exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: later.name, exact: true })).toHaveCount(0);
    const next = page.getByRole('button', { name: 'Next Organization page', exact: true });
    await next.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: later.name, exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: early.name, exact: true })).toHaveCount(0);
    await expect(next).toHaveCount(0);
    await page.getByRole('button', { name: 'First Organization page', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: early.name, exact: true })).toBeVisible();
    await page.goto(`/app/${later.id}`);
    await expect(page.getByRole('heading', { name: later.name, exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Create board', exact: true })).toBeVisible();
  }
});


test('PRD-03-TC-01/11/12/13: active Board directory keyboard paging on desktop and phone', async ({ page, context }) => {
  test.setTimeout(120_000); // Includes 51 acknowledged Board fixture writes.
  const headers = { 'X-StrataAI-Request': '1' };
  const credentials = { email: `paged-boards-${Date.now()}@example.test`, password: 'browser-paged-boards-correct-horse', displayName: 'Board directory owner' };
  expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
  const createdOrganization = await context.request.post('/organizations', { headers, data: { name: 'Paged Board council' } });
  expect(createdOrganization.status()).toBe(201);
  const organizationId = (await createdOrganization.json()).organization.id;
  for (let index = 0; index < 51; index++) {
    const created = await context.request.post('/boards', { headers, data: { organizationId, name: index === 0 ? "Paged\nwork Board 0" : `Paged work Board ${index}`, visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201);
  }
  const firstResponse = await context.request.get(`/organizations/${organizationId}/boards/directory`);
  expect(firstResponse.status()).toBe(200);
  const first = await firstResponse.json(); expect(first.items).toHaveLength(50); expect(first.nextCursor).toBeTruthy();
  const tailResponse = await context.request.get(`/organizations/${organizationId}/boards/directory?after=${first.nextCursor}`);
  expect(tailResponse.status()).toBe(200);
  const tail = await tailResponse.json(); expect(tail.items).toHaveLength(1); expect(tail.nextCursor).toBeNull();
  const early = first.items[0]; const later = tail.items[0];
  const label = (name: string) => name.replace(/\s+/g, ' ').trim();
  const multiline = [...first.items, ...tail.items].find(row => row.name.includes('\n'));
  expect(multiline).toBeTruthy();
  const multilineOnFirst = first.items.some((row: { id: string }) => row.id === multiline.id);
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 844 });
    await page.goto(`/app/${organizationId}`);
    await expect(page.getByRole('link', { name: label(early.name), exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: label(later.name), exact: true })).toHaveCount(0);
    if (multilineOnFirst) await expect(page.getByRole('link', { name: label(multiline.name), exact: true })).toBeVisible();
    const next = page.getByRole('button', { name: 'Next Board page', exact: true });
    await next.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: label(later.name), exact: true })).toBeVisible();
    if (!multilineOnFirst) await expect(page.getByRole('link', { name: label(multiline.name), exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: label(early.name), exact: true })).toHaveCount(0);
    await expect(next).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'First Board page', exact: true })).toBeFocused();
    await page.getByRole('button', { name: 'First Board page', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: label(early.name), exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Next Board page', exact: true }).click();
    const destination = page.getByRole('link', { name: label(later.name), exact: true });
    await expect(destination).toHaveAttribute('href', `/app/${organizationId}/boards/${later.id}`);
    await destination.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: later.name.includes('\n') ? /Paged\s+work Board 0/ : later.name, exact: true })).toBeVisible();
  }
});
