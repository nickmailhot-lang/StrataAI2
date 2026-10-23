# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: board-star.spec.ts >> PRD-04: personal star isolation, lost acknowledgment and later-state recovery at 1280px
- Location: tests/browser/board-star.spec.ts:9:7

# Error details

```
Error: expect(locator).toBeEnabled() failed

Locator: getByRole('button', { name: 'Star Board', exact: true })
Expected: enabled
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeEnabled" getByRole('button', { name: 'Star Board', exact: true }) with timeout 5000ms
  - waiting for getByRole('button', { name: 'Star Board', exact: true })

```

```yaml
- dialog "Board starring":
  - heading "Board starring" [level=2]
  - paragraph: Stars are personal to your account.
  - paragraph: You have not starred this Board.
  - status: This star change is unconfirmed. Retry the same change.
  - button "Check current star"
  - button "Review your star history"
  - button "Retry same star change"
- contentinfo "Application version": Version 0.1.0-1969 · Build 1688fda9720de34b6c94d0800ad96cfda1284b31
```

# Test source

```ts
  1   | import { disclosedWorkControl } from './workActionDisclosure';
  2   | import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
  3   | import { expect, test } from './releaseTest';
  4   | import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
  5   | import { trackBoardReads } from './boardReadTracker';
  6   | import { openAdmittedReview } from './keyboardAdmission';
  7   | 
  8   | for (const width of [1280, 390]) {
  9   |   test(`PRD-04: personal star isolation, lost acknowledgment and later-state recovery at ${width}px`, async ({ page, context, browser }) => {
  10  |     test.setTimeout(120_000);
  11  |     await page.setViewportSize({ width, height: 844 });
  12  |     const headers = { 'X-StrataAI-Request': '1' };
  13  |     const account = { email: `board-star-${width}-${Date.now()}@example.test`, password: 'star-correct-horse-battery', displayName: 'Star owner',
  14  |       locale: 'en-US', timezone: 'Pacific/Honolulu' };
  15  |     await registerVerifiedAccountFixture(context.request, account);
  16  |     const organization = await context.request.post('/organizations', { headers, data: { name: 'Personal star browser fixture' } });
  17  |     expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
  18  |     const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Independent stars', visibility: 'PUBLIC' } });
  19  |     expect(created.status()).toBe(201); const board = await created.json();
  20  |     const outsider = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
  21  |     const otherAccount = { ...account, email: `board-star-other-${width}-${Date.now()}@example.test`, displayName: 'Other star account', timezone: 'Asia/Tokyo' };
  22  |     await registerVerifiedAccountFixture(outsider.request, otherAccount);
  23  |     const restoreWorker = scopedBoardWorker(org);
  24  |     try {
  25  |       await waitForBoardDelivery(context.request, board.id);
  26  |       const path = `/app/${org}/boards/${board.id}`; const other = await outsider.newPage();
  27  |       const mirror = await context.newPage(); let initialPrivateFrame = false; let liveRevision = 0;
  28  |       const admissionReads = new Map([page, other, mirror].map(client => [client, trackBoardReads(client, board.id, path)]));
  29  |       mirror.on('websocket', socket => {
  30  |         if (!socket.url().includes('/boards/live/stars')) return;
  31  |         socket.on('framereceived', frame => {
  32  |           for (const value of frame.payload.toString().split('\x1e').filter(Boolean)) {
  33  |             try {
  34  |               const message = JSON.parse(value); const item = message.item;
  35  |               if (message.type !== 2 || item?.organizationId !== org || item?.boardId !== board.id || !Array.isArray(item.events)) continue;
  36  |               initialPrivateFrame = true;
  37  |               for (const event of item.events) if (event.eventType === 'BOARD_STARRED' && event.actorId === item.userId)
  38  |                 liveRevision = Math.max(liveRevision, event.version);
  39  |             } catch { /* SignalR handshake/control frames contain no star page. */ }
  40  |           }
  41  |         });
  42  |       });
  43  |       await page.goto(path); await other.goto(path); await mirror.goto(path);
  44  |       for (const client of [page, other, mirror]) {
  45  |         // The live bootstrap retires the first snapshot while it rechecks
  46  |         // admission. Require its protected follow-up read before keyboard use.
  47  |         await expect.poll(admissionReads.get(client)!).toBeGreaterThanOrEqual(2);
  48  |         await openAdmittedReview(
  49  |           (await disclosedWorkControl(client, 'Board actions', 'button', { name: 'Board starring', exact: true })),
  50  |           client.getByRole('dialog', { name: 'Board starring', exact: true }),
  51  |         );
  52  |         await expect(client.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
  53  |       }
  54  |       await expect.poll(() => initialPrivateFrame).toBe(true);
  55  |       const before = await context.request.get(`/boards/${board.id}`); expect(before.status()).toBe(200);
  56  |       const beforeState = await before.json();
  57  |       const keys: (string | undefined)[] = [];
  58  |       await page.route(`**/boards/${board.id}/star?*`, async route => {
  59  |         if (route.request().method() !== 'PUT') { await route.continue(); return; }
  60  |         keys.push(route.request().headers()['idempotency-key']);
  61  |         const acknowledgment = await route.fetch(); expect(acknowledgment.status()).toBe(204);
  62  |         if (keys.length === 1) {
  63  |           // A later authorized change commits after the first star, before its
  64  |           // acknowledgment reaches the original client.
  65  |           const unstar = await context.request.delete(`/boards/${board.id}/star?version=1`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } });
  66  |           expect(unstar.status()).toBe(204); await route.abort('failed');
  67  |         } else await route.fulfill({ response: acknowledgment });
  68  |       });
  69  |       await expect(page.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
  70  |       await page.getByRole('button', { name: 'Star Board', exact: true }).focus(); await page.keyboard.press('Enter');
  71  |       await expect(page.getByRole('button', { name: 'Retry same star change', exact: true })).toBeEnabled();
  72  |       await expect(page.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
  73  |       await expect(page.getByRole('button', { name: 'Done', exact: true })).toHaveCount(0);
  74  |       // Prove actual private WebSocket delivery to a second client for this
  75  |       // account, independently of the fallback HTTP polling timer.
  76  |       await expect.poll(() => liveRevision).toBe(2);
  77  |       await expect(other.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
  78  |       await other.getByRole('button', { name: 'Star Board', exact: true }).focus(); await other.keyboard.press('Enter');
  79  |       await expect(other.getByText('You have starred this Board.', { exact: true })).toBeVisible();
  80  |       // Private live delivery can trigger a bounded read while the other
  81  |       // account changes its star. Activate only after current parent admission
  82  |       // and the retry's actual keyboard focus have both recovered.
  83  |       // The modal hides its background from accessibility navigation; inspect
  84  |       // the parent's admission flag without interacting with that background.
  85  |       await expect(page.locator('[role="region"][aria-label="Board workspace"]')).toHaveAttribute('aria-busy', 'false');
  86  |       const retry = page.getByRole('button', { name: 'Retry same star change', exact: true });
  87  |       await expect(retry).toBeEnabled(); await retry.focus(); await expect(retry).toBeFocused();
  88  |       await page.keyboard.press('Enter');
> 89  |       await expect(page.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
      |                                                                                   ^ Error: expect(locator).toBeEnabled() failed
  90  |       await expect(page.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
  91  |       expect(keys).toHaveLength(2); expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/); expect(keys[1]).toBe(keys[0]);
  92  |       for (const [client, revisions] of [[page, 2], [other, 1], [mirror, 2]] as const) {
  93  |         const review = client.getByRole('button', { name: 'Review your star history', exact: true });
  94  |         const history = client.getByRole('region', { name: 'Your star history', exact: true });
  95  |         await openAdmittedReview(review, history);
  96  |         await expect(history.getByRole('listitem')).toHaveCount(revisions);
  97  |         await expect(history.getByText(`You changed your Board star (revision ${revisions}).`, { exact: true })).toBeVisible();
  98  |         const sourceReply = await client.request.get(`/boards/${board.id}/star/events?after=0`);
  99  |         expect(sourceReply.status()).toBe(200);
  100 |         const source = await sourceReply.json();
  101 |         expect(source.items).toHaveLength(revisions);
  102 |         const timezone = client === other ? 'Asia/Tokyo' : 'Pacific/Honolulu';
  103 |         for (const [index, item] of source.items.entries()) {
  104 |           const timestamp = history.locator('time').nth(index);
  105 |           await expect(timestamp).toHaveAttribute('datetime', item.createdAt);
  106 |           await expect(timestamp).toHaveText(await client.evaluate(({ instant, zone }) => new Intl.DateTimeFormat('en-US', {
  107 |             timeZone: zone, year: 'numeric', month: 'short', day: 'numeric',
  108 |             hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
  109 |           }).format(new Date(instant)), { instant: item.createdAt as string, zone: timezone }));
  110 |         }
  111 |         if (client === mirror) {
  112 |           const profileReply = await context.request.get('/me'); expect(profileReply.status()).toBe(200);
  113 |           const preferences = await context.request.patch('/me', { headers,
  114 |             data: { version: (await profileReply.json()).version, timezone: 'Asia/Tokyo' } });
  115 |           expect(preferences.status()).toBe(200);
  116 |           for (const [index, item] of source.items.entries()) {
  117 |             const timestamp = history.locator('time').nth(index);
  118 |             await expect(timestamp).toHaveText(await client.evaluate(instant => new Intl.DateTimeFormat('en-US', {
  119 |               timeZone: 'Asia/Tokyo', year: 'numeric', month: 'short', day: 'numeric',
  120 |               hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
  121 |             }).format(new Date(instant)), item.createdAt as string), { timeout: 20_000 });
  122 |             await expect(timestamp).toHaveAttribute('datetime', item.createdAt);
  123 |           }
  124 |           await expect(history.getByRole('listitem')).toHaveCount(revisions);
  125 |         }
  126 |         const close = client.getByRole('button', { name: 'Close star history', exact: true });
  127 |         await close.focus(); await client.keyboard.press('Enter'); await expect(review).toBeFocused();
  128 |       }
  129 |       for (const [client, starred] of [[context.request, false], [outsider.request, true]] as const) {
  130 |         const result = await client.get(`/boards/${board.id}/star`); expect(result.status()).toBe(200);
  131 |         expect((await result.json()).starred).toBe(starred);
  132 |       }
  133 |       const after = await context.request.get(`/boards/${board.id}`); expect(after.status()).toBe(200);
  134 |       const afterState = await after.json(); expect(afterState.board).toEqual(beforeState.board); expect(afterState.lists).toEqual(beforeState.lists);
  135 |       await expect(other.getByRole('button', { name: 'Unstar Board', exact: true })).toBeEnabled();
  136 |       await other.getByRole('button', { name: 'Unstar Board', exact: true }).focus(); await other.keyboard.press('Enter');
  137 |       await expect(other.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
  138 |       for (const client of [page, other, mirror]) {
  139 |         expect(await client.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  140 |         await expect(client.getByRole('button', { name: 'Done', exact: true })).toBeEnabled();
  141 |         await client.getByRole('button', { name: 'Done', exact: true }).focus(); await client.keyboard.press('Enter');
  142 |         await expect((await disclosedWorkControl(client, 'Board actions', 'button', { name: 'Board starring', exact: true }))).toBeFocused();
  143 |       }
  144 |       await mirror.close();
  145 |       await page.reload();
  146 |       await openAdmittedReview(
  147 |         (await disclosedWorkControl(page, 'Board actions', 'button', { name: 'Board starring', exact: true })),
  148 |         page.getByRole('dialog', { name: 'Board starring', exact: true }),
  149 |       );
  150 |       await expect(page.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
  151 |     } finally { await outsider.close(); restoreWorker(); }
  152 |   });
  153 | }
  154 | 
```