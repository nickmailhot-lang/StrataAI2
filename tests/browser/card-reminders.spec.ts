import { expect, test } from './releaseTest';
import AxeBuilder from '@axe-core/playwright';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { execFileSync } from 'node:child_process';

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

for (const width of [1280, 390]) {
  test(`PRD-12/17-TC-01/07/09/11/12: actual Worker due reminder reaches a private native inbox at ${width}px`, async ({ page, context }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `reminder-fire-${crypto.randomUUID()}@example.test`, password: 'reminder-fire-correct-horse', displayName: 'Reminder delivery fixture' };
    const registered = await context.request.post('/auth/register', { headers, data: credentials });
    expect(registered.status()).toBe(201); const account = await registered.json();
    // Email delivery is a separate contract. Activate only this disposable
    // account when the provider deliberately keeps its verification token private.
    if (account.verificationToken) {
      expect((await context.request.post('/auth/verify-email', { headers, data: { token: account.verificationToken } })).status()).toBe(200);
    } else {
      expect(process.env.CI).toBe('true'); expect(account.user.id).toMatch(/^[0-9a-f-]{36}$/);
      execFileSync('docker', ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c',
        'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'], {
        input: `UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,now()) WHERE id='${account.user.id}';`, stdio: 'pipe',
      });
    }
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Native reminder firing' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Reminder firing Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Reminder firing List' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Native due delivery' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const dueAt = new Date(Date.now() + 60_000).toISOString();
    expect((await context.request.patch(`/cards/${card}/dates`, { headers, data: { dueAt, dueTimezone: 'UTC', dueHasTime: true, dueComplete: false, version: 1 } })).status()).toBe(200);
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const inbox = await context.newPage(); await inbox.setViewportSize({ width, height: 844 });
      await inbox.goto(`/app/${org}/notifications`);
      await expect(inbox.getByText('0 unread on this page.', { exact: true })).toBeVisible();
      await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
      await page.getByRole('button', { name: 'Due reminder', exact: true }).press('Enter');
      const region = page.getByRole('region', { name: 'Personal due reminder' });
      await expect(region.getByText('You have no active due reminder.', { exact: true })).toBeVisible();
      await region.getByRole('combobox', { name: 'Reminder interval' }).press('Enter');
      await page.getByRole('option', { name: 'At the due time', exact: true }).press('Enter');
      const saving = page.waitForResponse(reply => new URL(reply.url()).pathname === `/cards/${card}/reminders` && reply.request().method() === 'POST');
      await region.getByRole('button', { name: 'Save due reminder' }).press('Enter');
      const saved = await saving; expect(saved.status()).toBe(200); const original = await saved.json();
      const key = saved.request().headers()['idempotency-key']; expect(key).toMatch(/^[0-9a-f-]{36}$/);
      const body = saved.request().postDataJSON();
      await expect(region.getByText('Due reminder saved.', { exact: true })).toBeVisible();
      await region.getByRole('button', { name: 'Due reminder', exact: true }).press('Enter');
      await expect(region.getByText('Your due reminder is scheduled.', { exact: true })).toBeVisible();
      await expect(region.getByText('Your due reminder was delivered.', { exact: true })).toBeVisible({ timeout: 90_000 });
      await expect(inbox.getByText('Due date reminder · Unread', { exact: true })).toBeVisible({ timeout: 30_000 });
      await expect(inbox.getByRole('article')).toHaveCount(1);
      await expect(inbox.getByRole('link', { name: 'Open Card', exact: true })).toHaveAttribute('href', `/app/${org}/boards/${board}/cards/${card}`);
      const fired = await context.request.get(`/cards/${card}/reminders`); expect(fired.status()).toBe(200);
      expect((await fired.json()).reminder).toMatchObject({ id: original.reminder.id, status: 'FIRED', version: 2, generation: 1 });
      const replay = await context.request.post(`/cards/${card}/reminders`, { headers: { ...headers, 'Idempotency-Key': key }, data: body });
      expect(replay.status()).toBe(200); expect(await replay.json()).toEqual(original);
      const beforeRead = await context.request.get(`/organizations/${org}/notifications`); expect(beforeRead.status()).toBe(200);
      const rows = (await beforeRead.json()).items; expect(rows).toHaveLength(1);
      expect(rows[0]).toMatchObject({ actorId: account.user.id, recipientId: account.user.id, entityId: card, type: 'REMINDER_FIRED', readAt: null });
      await inbox.getByRole('button', { name: 'Mark read', exact: true }).press('Enter');
      await expect(inbox.getByText('0 unread on this page.', { exact: true })).toBeVisible();
      const sync = await context.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(sync.status()).toBe(200);
      const journal = (await sync.json()).events;
      expect(journal.map((event: { eventType: string }) => event.eventType)).toEqual(['NOTIFICATION_CREATED', 'NOTIFICATION_READ']);
      expect(new Set(journal.map((event: { eventId: string }) => event.eventId)).size).toBe(2);
      for (const client of [page, inbox]) {
        expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        expect(await client.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
      await inbox.close();
    } finally { restoreWorker(); }
  });
}
