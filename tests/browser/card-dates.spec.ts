import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-12: persisted dates, viewing timezone and completion display across two sessions at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `card-dates-${width}-${Date.now()}@example.test`, password: 'date-browser-fixture-battery', displayName: 'Date viewer', timezone: 'UTC', locale: 'en-US' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Date Organization' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Date Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Date List' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Dated work' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const dateInput = { startAt: null, dueAt: '2040-01-02', dueTimezone: 'Pacific/Honolulu', dueHasTime: false, dueComplete: false, version: 1 };
    const route = `/app/${org}/boards/${board}/cards/${card}`;
    await page.goto(route);
    const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
    await page.route(`**/cards/${card}/dates`, async intercepted => {
      attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
      const result = await intercepted.fetch(); expect(result.status()).toBe(200);
      if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response: result });
    });
    const edit = page.getByRole('button', { name: 'Edit dates' }); await expect(edit).toBeEnabled();
    await edit.focus(); await expect(edit).toBeFocused(); await page.keyboard.press('Enter');
    await page.getByLabel('Due date', { exact: true }).fill('2040-01-02');
    await page.getByLabel('Date timezone', { exact: true }).fill('Pacific/Honolulu');
    await page.getByRole('button', { name: 'Save dates', exact: true }).click();
    const retry = page.getByRole('button', { name: 'Retry date save' }); await expect(retry).toBeEnabled();
    await expect(page.getByLabel('Due date', { exact: true })).toBeDisabled();
    await retry.focus(); await expect(retry).toBeFocused(); await page.keyboard.press('Enter');
    await expect(page.getByText('Dates saved.', { exact: true })).toBeVisible();
    expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
    expect(JSON.parse(attempts[0].body!)).toMatchObject({ dueAt: '2040-01-02', dueTimezone: 'Pacific/Honolulu', version: 1 });
    await page.unroute(`**/cards/${card}/dates`);
    const region = page.getByRole('region', { name: 'Card dates' });
    await expect(region).toContainText('Due Jan 3, 2040'); await expect(region).toContainText('Upcoming');
    await expect(region).toContainText('Viewing timezone: UTC. Date context: Pacific/Honolulu.');
    const other = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await other.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await other.newPage(); await peer.goto(route);
      const peerDates = peer.getByRole('region', { name: 'Card dates' }); await expect(peerDates).toContainText('Upcoming');
      const completed = await other.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { ...dateInput, version: 2, dueComplete: true } });
      expect(completed.status()).toBe(200);
      // Explicit refresh checks persisted canonical state; this scenario does
      // not stand in for separate recipient-private realtime acceptance.
      await page.reload(); await peer.reload();
      await expect(region).toContainText('Complete'); await expect(peerDates).toContainText('Complete');
      const me = await other.request.get('/me'); expect(me.status()).toBe(200); const profile = await me.json();
      expect((await other.request.patch('/me', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: profile.version, timezone: 'Pacific/Honolulu' } })).status()).toBe(200);
      await page.reload(); await peer.reload();
      await expect(region).toContainText('Due Jan 2, 2040'); await expect(peerDates).toContainText('Due Jan 2, 2040');
      await expect(region).toContainText('Viewing timezone: Pacific/Honolulu.'); await expect(region).toContainText('Complete');
      await expect(region).not.toContainText('1:59');
      const clear = await other.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 3, dueHasTime: false, dueComplete: false } });
      expect(clear.status()).toBe(200); await page.reload(); await peer.reload();
      await expect(region).toHaveCount(0); await expect(peerDates).toHaveCount(0);
    } finally { await other.close(); }
  });
}
