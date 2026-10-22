# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: invitation-history-clock.spec.ts >> PRD-01/02/03/04-TC-03/06/11/12: invitation history rejects normalized source clocks and recovers actual records at 390px
- Location: tests/browser/invitation-history-clock.spec.ts:8:7

# Error details

```
Error: expect(locator).toBeFocused() failed

Locator: getByRole('button', { name: 'Cancel', exact: true })
Expected: focused
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeFocused" getByRole('button', { name: 'Cancel', exact: true }) with timeout 5000ms
  - waiting for getByRole('button', { name: 'Cancel', exact: true })
    - locator resolved to <button tabindex="0" type="button" class="MuiButtonBase-root MuiButton-root MuiButton-text MuiButton-sizeMedium MuiButton-colorPrimary css-1pny2ut">Cancel</button>
    - unexpected value "inactive"
    - locator resolved to <button disabled tabindex="-1" type="button" class="MuiButtonBase-root Mui-disabled MuiButton-root MuiButton-text MuiButton-sizeMedium MuiButton-colorPrimary css-1pny2ut">Cancel</button>
    2 × unexpected value "inactive"
      - locator resolved to <button tabindex="0" type="button" class="MuiButtonBase-root MuiButton-root MuiButton-text MuiButton-sizeMedium MuiButton-colorPrimary css-1pny2ut">Cancel</button>
    - unexpected value "inactive"

```

```yaml
- link "Skip to main content":
  - /url: "#internal-main-content"
- banner:
  - button "Open navigation"
  - heading "StrataAI2" [level=1]
  - text: "Organization: History clock council"
  - link "Search Cards":
    - /url: /app/951f6ee3-4a78-4c1d-98cf-7fb66b73fda0/search
  - link "Open profile":
    - /url: /app/951f6ee3-4a78-4c1d-98cf-7fb66b73fda0/profile
  - link "Open notifications":
    - /url: /app/951f6ee3-4a78-4c1d-98cf-7fb66b73fda0/notifications
- main:
  - link "Organization members":
    - /url: /app/951f6ee3-4a78-4c1d-98cf-7fb66b73fda0/members
  - heading "Issued invitations" [level=1]
  - alert: Current invitations checked. Review an invitation again before confirming revocation.
  - button "Refresh invitations"
  - link "Create invitation":
    - /url: /app/951f6ee3-4a78-4c1d-98cf-7fb66b73fda0/invite
  - paragraph: Email acknowledgment does not prove inbox delivery or grant access. Revocation prevents acceptance; existing membership is managed separately.
  - article:
    - heading "clock-recipient-false-390-1792680550192@example.test" [level=2]
    - paragraph: Internal Organization · member
    - paragraph: Awaiting acceptance
    - paragraph: No email delivery recorded
    - paragraph: "Expires: Oct 29, 2026, 07:49 PDT"
    - button "Revoke invitation for clock-recipient-false-390-1792680550192@example.test"
  - button "Previous invitations" [disabled]
  - button "Next invitations" [disabled]
- contentinfo "Application version": Version 0.1.0-1970 · Build da9f1dcdd873a31efb93738bb43d5315e5eb8696
```

# Test source

```ts
  1  | import AxeBuilder from '@axe-core/playwright';
  2  | import { expect, test } from './releaseTest';
  3  | import { openAdmittedReview } from './keyboardAdmission';
  4  | import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
  5  | import { trackInvitationAdmission } from './invitationAdmissionTracker';
  6  | 
  7  | for (const width of [1280, 390]) {
  8  |   test(`PRD-01/02/03/04-TC-03/06/11/12: invitation history rejects normalized source clocks and recovers actual records at ${width}px`, async ({ page, context }) => {
  9  |     test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
  10 |     const headers = { 'X-StrataAI-Request': '1' };
  11 |     const account = { email: `history-clock-${width}-${Date.now()}@example.test`, password: 'history-clock-correct-horse', displayName: 'History clock Owner' };
  12 |     const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
  13 |     const actor = (await registered.json()).user.id;
  14 |     expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  15 |     const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'History clock council' } });
  16 |     expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
  17 |     const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private history clock Board', visibility: 'PRIVATE' } });
  18 |     expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
  19 |     let documents = 0; let writes = 0;
  20 |     page.on('request', request => {
  21 |       if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
  22 |       if (request.method() === 'DELETE' && new URL(request.url()).pathname.includes('/invitations/')) writes++;
  23 |     });
  24 |     const restore = scopedBoardWorker(org);
  25 |     try {
  26 |       for (const boardSurface of [false, true]) {
  27 |         const root = boardSurface ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
  28 |         const recipient = `clock-recipient-${boardSurface}-${width}-${Date.now()}@example.test`;
  29 |         const issued = await context.request.post(root, { headers, data: boardSurface ? { email: recipient, role: 'MEMBER' } : { email: recipient, surface: 'INTERNAL', targetRole: 'MEMBER' } });
  30 |         expect(issued.status()).toBe(201); const invitation = (await issued.json()).id;
  31 |         await waitForBoardDelivery(context.request, board);
  32 |         const before = await context.request.get(root); expect(before.status()).toBe(200); const canonical = await before.json();
  33 |         expect(canonical.items).toHaveLength(1); expect(canonical.items[0].id).toBe(invitation);
  34 |         let malformed = true; let corruptReads = 0;
  35 |         const matches = (url: URL) => url.pathname === root;
  36 |         await page.route(matches, async route => {
  37 |           if (route.request().method() !== 'GET') return route.continue();
  38 |           const reply = await route.fetch(); expect(reply.status()).toBe(200); const body = await reply.json();
  39 |           if (malformed) { corruptReads++; body.items = body.items.map((row: { id: string }) => row.id === invitation ? { ...row, expiresAt: '2035-02-30T12:00:00Z' } : row); }
  40 |           await route.fulfill({ response: reply, json: body });
  41 |         });
  42 |         const path = boardSurface ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`;
  43 |         const admitted = trackInvitationAdmission(page, org, actor, boardSurface ? board : undefined, path, root);
  44 |         await page.goto(path);
  45 |         await expect(page.getByText('Unable to confirm invitation history. Please retry.', { exact: true })).toBeVisible();
  46 |         expect(corruptReads).toBeGreaterThan(0); expect(writes).toBe(0);
  47 |         await expect(page.getByText(recipient, { exact: true })).toHaveCount(0);
  48 |         if (boardSurface) await expect(page.getByRole('heading', { name: 'Private history clock Board', exact: true })).toHaveCount(0);
  49 |         const revoke = page.getByRole('button', { name: `Revoke invitation for ${recipient}`, exact: true }); await expect(revoke).toHaveCount(0);
  50 |         malformed = false;
  51 |         await openAdmittedReview(page.getByRole('button', { name: 'Refresh invitations', exact: true }), page.getByRole('heading', { name: recipient, exact: true }));
  52 |         await expect.poll(admitted.ready, { timeout: 30_000 }).toBe(true); await expect(revoke).toBeEnabled();
  53 |         const dialog = page.getByRole('dialog', { name: 'Revoke invitation?', exact: true });
> 54 |         await openAdmittedReview(revoke, dialog); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
     |                                                                                                                           ^ Error: expect(locator).toBeFocused() failed
  55 |         expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  56 |         await page.keyboard.press('Enter'); await expect(dialog).toHaveCount(0); expect(writes).toBe(0);
  57 |         const after = await context.request.get(root); expect(after.status()).toBe(200); const retained = await after.json();
  58 |         expect(retained.items).toHaveLength(1); expect(retained.nextCursor).toBe(canonical.nextCursor);
  59 |         // Mail delivery may advance independently; all source history fields
  60 |         // and clocks must remain byte-for-byte equal after display recovery.
  61 |         const sourceFields = (row: Record<string, unknown>) => Object.fromEntries(Object.entries(row).filter(([key]) => key !== 'deliveryState'));
  62 |         expect(sourceFields(retained.items[0])).toEqual(sourceFields(canonical.items[0]));
  63 |         await page.unroute(matches);
  64 |       }
  65 |       expect(documents).toBe(2); expect(writes).toBe(0);
  66 |     } finally { restore(); }
  67 |   });
  68 | }
  69 | 
```