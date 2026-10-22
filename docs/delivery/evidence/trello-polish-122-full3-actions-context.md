# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: organization-actions.spec.ts >> PRD-03-VS-06-T-03/AC-01/02/03: default permitted Organization actions and canonical return at 390px
- Location: tests/browser/organization-actions.spec.ts:11:7

# Error details

```
Error: expect(locator).toHaveJSProperty(expected) failed

Locator:  locator('summary').filter({ hasText: /^Organization actions$/ }).locator('..')
Expected: false
Received: true
Timeout:  5000ms

Call log:
  - Expect "toHaveJSProperty" locator('summary').filter({ hasText: /^Organization actions$/ }).locator('..') with timeout 5000ms
  - waiting for locator('summary').filter({ hasText: /^Organization actions$/ }).locator('..')
    11 × locator resolved to <details open="" class="MuiBox-root css-jprazl">…</details>
       - unexpected value "true"

```

```yaml
- group:
  - text: Organization actions
  - link "Archived boards":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/archived-boards
  - link "Leave Organization":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/leave
  - link "Organization lifecycle":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/operational-lifecycle
  - link "Organization settings":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/settings
  - link "Organization configuration":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/configuration
  - link "Retention policies":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/retention-policies
  - link "Retention holds":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/retention-holds
  - link "Organization members":
    - /url: /app/c0b01cbe-cbff-2e45-ce79-4d69970b21eb/members
```

# Test source

```ts
  1   | import AxeBuilder from '@axe-core/playwright';
  2   | import type { Page } from '@playwright/test';
  3   | import { expect, test } from './releaseTest';
  4   | import { registerNotificationAccount } from './notificationAccountFixture';
  5   | import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
  6   | import { openOrganizationActions } from './organizationActionAdmission';
  7   | import { openAdmittedReview, tabAdmittedFocus } from './keyboardAdmission';
  8   | 
  9   | const rollback = process.env.STRATAAI_E2E_WORK_UX_ROLLBACK === '1';
  10  | for (const width of [1280, 768, 390, 320]) {
  11  |   test(`PRD-03-VS-06-T-03/AC-01/02/03: ${rollback ? 'rollback' : 'default'} permitted Organization actions and canonical return at ${width}px`, async ({ page, context, browser }) => {
  12  |     test.setTimeout(120_000); await page.setViewportSize({ width, height: 900 });
  13  |     const participant = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 900 } });
  14  |     const headers = { 'X-StrataAI-Request': '1' }, mutationHeaders = () => ({ ...headers, 'Idempotency-Key': crypto.randomUUID() });
  15  |     let releaseReads = () => {}, holding = false, gate = Promise.resolve(), heldReads = 0;
  16  |     let restoreWorker = () => {};
  17  |     try {
  18  |       const account = { email: `organization-actions-${crypto.randomUUID()}@example.test`, password: 'organization-actions-correct-horse', displayName: 'Organization action owner' };
  19  |       const invited = { ...account, email: `organization-actions-participant-${crypto.randomUUID()}@example.test`, displayName: 'Current action participant' };
  20  |       await registerNotificationAccount(context.request, account); await registerNotificationAccount(participant.request, invited);
  21  |       expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
  22  |       const created = await context.request.post('/organizations', { headers: mutationHeaders(), data: { name: 'Organization actions scope' } });
  23  |       expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
  24  |       const createdBoard = await context.request.post('/boards', { headers: mutationHeaders(), data: { organizationId: org, name: 'Permitted action Board', visibility: 'ORGANIZATION' } });
  25  |       expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
  26  |       const createdList = await context.request.post(`/boards/${board}/lists`, { headers: mutationHeaders(), data: { name: 'Permitted action List' } });
  27  |       expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
  28  |       const createdCard = await context.request.post(`/lists/${list}/cards`, { headers: mutationHeaders(), data: { title: 'Permitted action Card' } });
  29  |       expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
  30  |       restoreWorker = scopedBoardWorker(org);
  31  |       await waitForBoardDelivery(context.request, board);
  32  |       const directoryPath = `/app/${org}`, organizationPath = `/organizations/${org}`, boardPath = `${directoryPath}/boards/${board}`;
  33  |       async function canonical() {
  34  |         const response = await context.request.get(`/boards/${board}`); expect(response.status()).toBe(200);
  35  |         const value = await response.json(); expect(value.board).toMatchObject({ id: board });
  36  |         return { board: value.board, lists: value.lists };
  37  |       }
  38  |       const before = await canonical(), graphWrites: string[] = [];
  39  |       const observe = (client: Page) => client.on('request', request => {
  40  |         const path = new URL(request.url()).pathname;
  41  |         if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) && [board, list, card].some(id => path.split('/').includes(id)))
  42  |           graphWrites.push(`${request.method()} ${path}`);
  43  |       });
  44  |       observe(page);
  45  |       async function layout(client: Page) {
  46  |         expect(await client.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  47  |         expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  48  |       }
  49  |       async function currentActions(client: Page, role: 'Owner' | 'Admin' | 'Member') {
  50  |         const create = client.getByRole('button', { name: 'Create board', exact: true }); await expect(create).toBeVisible();
  51  |         const summary = client.locator('summary').filter({ hasText: /^Organization actions$/ });
  52  |         if (rollback) await expect(summary).toHaveCount(0);
  53  |         else {
  54  |           await expect(summary).toBeVisible();
  55  |           // Admission may withdraw both controls after visibility was observed.
  56  |           // Compare contemporaneous admitted nodes, never an obsolete handle.
  57  |           await expect.poll(() => create.evaluateAll(elements => {
  58  |             const current = elements.length === 1 ? elements[0] : undefined;
  59  |             const summary = Array.from(document.querySelectorAll('summary')).find(element => element.textContent?.trim() === 'Organization actions');
  60  |             return !!current && !!summary && current.isConnected && summary.isConnected
  61  |               && Boolean(current.compareDocumentPosition(summary) & Node.DOCUMENT_POSITION_FOLLOWING);
  62  |           })).toBe(true);
  63  |         }
  64  |         const permitted = client.getByRole('link', { name: role === 'Member' ? 'Leave Organization' : 'Organization settings', exact: true });
  65  |         await openOrganizationActions(client, permitted); await expect(permitted).toBeVisible();
  66  |         if (role === 'Member') await expect(client.getByRole('link', { name: 'Organization settings', exact: true, includeHidden: true })).toHaveCount(0);
  67  |         else await expect(client.getByRole('link', { name: 'Organization settings', exact: true })).toHaveAttribute('href', `${directoryPath}/settings`);
  68  |         if (role === 'Owner') await expect(client.getByRole('link', { name: 'Request Organization deletion', exact: true })).toBeVisible();
  69  |         else await expect(client.getByRole('link', { name: 'Request Organization deletion', exact: true, includeHidden: true })).toHaveCount(0);
  70  |         if (!rollback) {
  71  |           // Collapsed actions stay mounted but leave keyboard/accessibility navigation.
  72  |           const details = summary.locator('..');
  73  |           const permittedName = role === 'Member' ? 'Leave Organization' : 'Organization settings';
  74  |           await summary.focus(); await client.keyboard.press('Enter');
> 75  |           await expect(details).toHaveJSProperty('open', false);
      |                                 ^ Error: expect(locator).toHaveJSProperty(expected) failed
  76  |           await expect(client.getByRole('link', { name: permittedName, exact: true, includeHidden: true })).toHaveCount(1);
  77  |           await expect(permitted).toHaveCount(0);
  78  |           await summary.focus(); await expect(summary).toBeFocused();
  79  |           const nextBoard = client.getByRole('link', { name: 'Permitted action Board', exact: true });
  80  |           await tabAdmittedFocus(summary, nextBoard, 'Tab');
  81  |           await expect(client.getByRole('link', { name: 'Permitted action Board', exact: true })).toBeFocused();
  82  |           await tabAdmittedFocus(nextBoard, summary, 'Shift+Tab'); await expect(summary).toBeFocused();
  83  |           await openOrganizationActions(client, permitted);
  84  |           await expect(details).toHaveJSProperty('open', true); await expect(permitted).toBeVisible();
  85  |         }
  86  |         const focusTarget = rollback ? permitted : summary;
  87  |         await client.keyboard.press('Tab');
  88  |         // A foreground admission read can retire the focused control. Establish
  89  |         // only current read-only focus, then measure that same connected node.
  90  |         await expect(async () => {
  91  |           await focusTarget.focus({ timeout: 500 }); await expect(focusTarget).toBeFocused({ timeout: 500 });
  92  |           expect(await focusTarget.evaluateAll(elements => {
  93  |             const element = elements.length === 1 ? elements[0] : undefined;
  94  |             if (!element?.isConnected || element !== document.activeElement) return false;
  95  |             const box = element.getBoundingClientRect(), header = document.querySelector('header')?.getBoundingClientRect();
  96  |             return box.height >= 44 && box.top >= (header?.bottom ?? 0) && box.bottom <= window.innerHeight
  97  |               && element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2));
  98  |           })).toBe(true);
  99  |         }).toPass({ timeout: 5_000 });
  100 |         await layout(client);
  101 |         await client.screenshot({ path: test.info().outputPath(`organization-actions-${rollback ? 'rollback' : 'default'}-${role.toLowerCase()}-viewport-${width}.png`), fullPage: false });
  102 |       }
  103 |       await page.goto(directoryPath);
  104 |       if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
  105 |       await currentActions(page, 'Owner');
  106 |       async function issue(role: 'ADMIN' | 'MEMBER') {
  107 |         const issued = await context.request.post(`${organizationPath}/invitations`, { headers: mutationHeaders(), data: { email: invited.email, surface: 'INTERNAL', targetRole: role } });
  108 |         expect(issued.status()).toBe(201); const id = (await issued.json()).id;
  109 |         expect((await participant.request.post(`/me/invitations/${id}/accept`, { headers: mutationHeaders() })).status()).toBe(200);
  110 |       }
  111 |       await issue('ADMIN');
  112 |       const participantPage = await participant.newPage(); observe(participantPage); let documents = 0;
  113 |       participantPage.on('request', request => { if (request.isNavigationRequest() && request.frame() === participantPage.mainFrame()) documents++; });
  114 |       await participantPage.goto(directoryPath);
  115 |       if (width === 768) await participantPage.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
  116 |       await currentActions(participantPage, 'Admin');
  117 |       await openAdmittedReview(participantPage.getByRole('link', { name: 'Organization settings', exact: true }), participantPage.getByLabel(/^Organization name/));
  118 |       await expect(participantPage).toHaveURL(new RegExp(`${directoryPath}/settings$`));
  119 |       await expect(participantPage.getByLabel(/^Organization name/)).toHaveValue('Organization actions scope');
  120 |       await participantPage.goBack(); await expect(participantPage).toHaveURL(new RegExp(directoryPath + '$'));
  121 |       await currentActions(participantPage, 'Admin'); expect(documents).toBe(1);
  122 |       await participantPage.route(url => url.pathname === organizationPath, async route => {
  123 |         if (holding && route.request().method() === 'GET') { heldReads++; await gate; }
  124 |         await route.continue();
  125 |       });
  126 |       gate = new Promise<void>(resolve => { releaseReads = resolve; }); holding = true;
  127 |       await issue('MEMBER');
  128 |       await expect.poll(() => heldReads, { timeout: 30_000 }).toBeGreaterThan(0);
  129 |       await expect(participantPage.getByRole('link', { name: 'Organization settings', exact: true, includeHidden: true })).toHaveCount(0);
  130 |       await expect(participantPage.locator('summary').filter({ hasText: /^Organization actions$/ })).toHaveCount(0);
  131 |       holding = false; releaseReads();
  132 |       await currentActions(participantPage, 'Member'); expect(documents).toBe(1);
  133 |       const denied = await participant.request.patch(organizationPath, { headers: mutationHeaders(), data: { name: 'Unauthorized replacement', description: null, logoUrl: null, version: 1 } });
  134 |       expect(denied.status()).toBe(404); expect((await denied.json()).code).toBe('organization_not_found');
  135 |       const unchanged = await context.request.get(organizationPath); expect(unchanged.status()).toBe(200);
  136 |       expect((await unchanged.json()).organization.name).toBe('Organization actions scope');
  137 |       await participantPage.goto(`${directoryPath}/settings`);
  138 |       await expect(participantPage.getByText('Organization settings are unavailable to your account.', { exact: true })).toBeVisible();
  139 |       await expect(participantPage.getByLabel(/^Organization name/)).toHaveCount(0);
  140 |       await participantPage.goBack(); await expect(participantPage).toHaveURL(new RegExp(directoryPath + '$'));
  141 |       if (width === 768) await participantPage.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
  142 |       const boardLink = participantPage.getByRole('link', { name: 'Permitted action Board', exact: true });
  143 |       const cardLink = participantPage.getByRole('link', { name: 'Permitted action Card', exact: true });
  144 |       // Foreground reads can replace the directory link between focus and Enter.
  145 |       // Admit only this read-only opening until its real Board target appears.
  146 |       await openAdmittedReview(boardLink, cardLink);
  147 |       await expect(participantPage).toHaveURL(new RegExp(boardPath + '$'));
  148 |       await cardLink.focus(); await cardLink.press('Enter');
  149 |       await expect(participantPage.getByRole('dialog', { name: 'Card details', exact: true })).toBeVisible();
  150 |       await expect(participantPage.getByRole('textbox', { name: 'Card title', exact: true })).toHaveValue('Permitted action Card');
  151 |       await participantPage.goBack(); await expect(participantPage).toHaveURL(new RegExp(boardPath + '$'));
  152 |       await expect(cardLink).toBeFocused(); await layout(participantPage);
  153 |       expect(await canonical()).toEqual(before); expect(graphWrites).toEqual([]);
  154 |       expect((await participant.request.post('/auth/logout', { headers })).status()).toBe(204);
  155 |       await participantPage.evaluate(() => window.dispatchEvent(new Event('focus')));
  156 |       await expect(participantPage).toHaveURL(/\/login$/);
  157 |       await expect(participantPage.getByText('Permitted action Card', { exact: true })).toHaveCount(0);
  158 |       await expect(participantPage.getByRole('navigation', { name: 'Internal application navigation', exact: true })).toHaveCount(0);
  159 |       expect(graphWrites).toEqual([]);
  160 |     } finally {
  161 |       holding = false; releaseReads();
  162 |       try { restoreWorker(); } finally { await participant.close(); }
  163 |     }
  164 |   });
  165 | }
  166 | 
```