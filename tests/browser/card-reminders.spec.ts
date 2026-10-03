import { expect, test } from './releaseTest';
import AxeBuilder from '@axe-core/playwright';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-12: keyboard personal Reminder recovery and live cancellation across clients at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `reminders-${width}-${Date.now()}@example.test`, password: 'reminder-fixture-correct-horse', displayName: 'Reminder user' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Reminder Organization' } });
    expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
    const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Reminder Board', visibility: 'PRIVATE' } });
    expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
    const listResult = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Reminder List' } });
    expect(listResult.status()).toBe(201); const list = (await listResult.json()).id;
    const cardResult = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Reminder Card' } });
    expect(cardResult.status()).toBe(201); const card = (await cardResult.json()).id;
    expect((await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { dueAt: '2040-01-02T12:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: false, version: 1 } })).status()).toBe(200);
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const peer = await context.newPage(); await peer.setViewportSize({ width, height: 844 });
      const path = `/app/${org}/boards/${board}/cards/${card}`;
      await peer.goto(path); await page.goto(path);
      for (const client of [page, peer]) {
        await expect(client.getByText('Live updates connected.', { exact: true })).toBeVisible();
        const open = client.getByRole('button', { name: 'Due reminder', exact: true });
        await expect(open).toBeEnabled(); await open.press('Enter');
        await expect(client.getByText('You have no active due reminder.', { exact: true })).toBeVisible();
      }
      const region = page.getByRole('region', { name: 'Personal due reminder' });
      const peerRegion = peer.getByRole('region', { name: 'Personal due reminder' });
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/cards/${card}/reminders`, async intercepted => {
        if (intercepted.request().method() !== 'POST') { await intercepted.continue(); return; }
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (attempts.length === 1) await intercepted.abort('failed'); else await intercepted.fulfill({ response });
      });
      await region.getByRole('combobox', { name: 'Reminder interval' }).press('Enter');
      await page.getByRole('option', { name: '1 hour before', exact: true }).press('Enter');
      await region.getByRole('button', { name: 'Save due reminder' }).press('Enter');
      const retry = region.getByRole('button', { name: 'Retry reminder change' }); await expect(retry).toBeEnabled();
      await expect(region.getByRole('combobox', { name: 'Reminder interval' })).toHaveCount(0);
      await expect(region.getByRole('button', { name: 'Close reminder' })).toHaveCount(0);
      // The peer recovers its own private choice through Board invalidation,
      // with no reload and no shared-stream Reminder identity/type disclosure.
      await expect(peerRegion.getByText('Your due reminder is scheduled.', { exact: true })).toBeVisible();
      await expect(retry).toBeEnabled(); await retry.press('Enter'); await expect(region.getByText('Due reminder saved.', { exact: true })).toBeVisible();
      await expect(region.getByRole('button', { name: 'Due reminder', exact: true })).toBeFocused();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ intervalCode: '1_HOUR', enabled: true, cardVersion: 2, version: 0 });
      expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      await region.getByRole('button', { name: 'Due reminder', exact: true }).press('Enter');
      await expect(region.getByText('Your due reminder is scheduled.', { exact: true })).toBeVisible();
      await expect(peerRegion.getByRole('button', { name: 'Cancel due reminder' })).toBeEnabled();
      await peerRegion.getByRole('button', { name: 'Cancel due reminder' }).press('Enter');
      await expect(peerRegion.getByText('Due reminder cancelled.', { exact: true })).toBeVisible();
      await expect(region.getByText('You have no active due reminder.', { exact: true })).toBeVisible();
      await expect(region.getByRole('button', { name: 'Cancel due reminder' })).toHaveCount(0);
      const canonical = await context.request.get(`/cards/${card}/reminders`); expect(canonical.status()).toBe(200);
      expect((await canonical.json()).reminder).toMatchObject({ status: 'CANCELLED', enabled: false, version: 2, generation: 2, cardId: card });
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await peer.close();
    } finally { restoreWorker(); }
  });
}
