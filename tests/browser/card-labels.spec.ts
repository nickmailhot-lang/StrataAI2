import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-10: Card labels have keyboard-readable names and reflect persisted deletion at ${width}px`, async ({ page, context }) => {
    test.setTimeout(60_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `card-labels-${width}-${Date.now()}@example.test`, password: 'card-label-correct-horse-battery', displayName: 'Label reader' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Label Organization' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Label Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Labeled work' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const labels: string[] = []; let version = 1;
    for (const [name, color] of [['Priority', 'red'], ['', 'blue']]) {
      const created = await context.request.post(`/boards/${board}/labels`, { headers, data: { name, color } });
      expect(created.status()).toBe(201); const id = (await created.json()).id; labels.push(id);
      const assigned = await context.request.put(`/cards/${card}/labels/${id}?version=${version}`, { headers });
      expect(assigned.status()).toBe(200); version = (await assigned.json()).card.version;
    }
    const path = `/app/${org}/boards/${board}/cards/${card}`;
    await page.goto(path);
    const show = page.getByRole('button', { name: 'Show labels', exact: true });
    await show.focus(); await page.keyboard.press('Enter');
    await expect(page.getByLabel('Priority, red', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Unnamed label, blue', { exact: true })).toBeVisible();
    await expect(page.getByText('blue label', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Hide labels', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByLabel('Priority, red', { exact: true })).toHaveCount(0);
    for (const id of labels) expect((await context.request.delete(`/labels/${id}?version=1&confirmed=true`, { headers })).status()).toBe(200);
    await page.reload(); await show.focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('No labels assigned.', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Priority, red', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('textbox', { name: 'Card title' })).toHaveValue('Labeled work');
  });
}
