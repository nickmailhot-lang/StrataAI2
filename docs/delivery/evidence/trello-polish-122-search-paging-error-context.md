# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: trello-card-search.spec.ts >> PRD-16 Trello Search surfaces preserve drafts, paging and denial at 320px
- Location: tests/browser/trello-card-search.spec.ts:115:7

# Error details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: "detail-search-next"
Received: null
```

# Page snapshot

```yaml
- generic [ref=e2]:
  - generic [ref=e4]:
    - link "Skip to main content" [ref=e5] [cursor=pointer]:
      - /url: "#internal-main-content"
    - banner [ref=e6]:
      - generic [ref=e7]:
        - button "Open navigation" [ref=e8] [cursor=pointer]
        - heading "StrataAI2" [level=1] [ref=e11]
        - generic "Cedar Court" [ref=e12]: "Organization: Cedar Court"
        - link "Search Cards" [ref=e13] [cursor=pointer]:
          - /url: /app/11111111-1111-4111-8111-111111111111/search
        - link "Open profile" [ref=e16] [cursor=pointer]:
          - /url: /app/11111111-1111-4111-8111-111111111111/profile
        - link "Open notifications" [ref=e19] [cursor=pointer]:
          - /url: /app/11111111-1111-4111-8111-111111111111/notifications
    - main [ref=e22]:
      - region "Global Card search" [ref=e24]:
        - generic [ref=e25]:
          - heading "Search Cards" [level=2] [ref=e26]
          - paragraph [ref=e27]: Search across your accessible Organizations and Boards.
        - generic [ref=e28]:
          - generic [ref=e29]:
            - generic [ref=e30]: Card text
            - generic [ref=e31]:
              - textbox "Card text" [ref=e32]: unsent draft
              - group [aria-hidden]:
                - generic: Card text
          - generic [ref=e33]:
            - generic: Label name
            - generic [ref=e34]:
              - textbox "Label name" [ref=e35]
              - group [aria-hidden]:
                - generic: Label name
          - generic [ref=e36]:
            - generic: Member name
            - generic [ref=e37]:
              - textbox "Member name" [ref=e38]
              - group [aria-hidden]:
                - generic: Member name
          - generic [ref=e39]:
            - generic [ref=e40]: Match criteria
            - generic [ref=e41]:
              - combobox "Match criteria" [ref=e42] [cursor=pointer]: Match all
              - textbox [aria-hidden]: all
              - group [aria-hidden]:
                - generic: Match criteria
          - generic [ref=e43]:
            - generic [ref=e44]: Search scope
            - generic [ref=e45]:
              - combobox "Search scope" [ref=e46] [cursor=pointer]: Archived work
              - textbox [aria-hidden]: archived
              - group [aria-hidden]:
                - generic: Search scope
          - button "Search" [ref=e47] [cursor=pointer]
        - status [active] [ref=e48]:
          - paragraph [ref=e49]: 1 result on this page. Search complete.
        - generic [ref=e50]:
          - link "Inspect the parkade water leak" [ref=e51] [cursor=pointer]:
            - /url: /app/11111111-1111-4111-8111-111111111111/boards/33333333-3333-4333-8333-333333333333/cards/55555555-5555-4555-8555-555555555555
          - paragraph [ref=e52]: Council operations / To do
          - paragraph [ref=e53]: "Labels: None"
          - paragraph [ref=e54]: "Members: None"
          - paragraph [ref=e55]: No deadline
        - button "Refresh results" [ref=e57] [cursor=pointer]
  - contentinfo "Application version" [ref=e58]:
    - generic [ref=e59]: Version 0.1.0-1969 · Build 1688fda9720de34b6c94d0800ad96cfda1284b31
```

# Test source

```ts
  51  |       source.searches.push(url.search);
  52  |       return route.fulfill({ status: source.searchFailure || 200, json: { nextCursor: url.searchParams.has('after') ? null : 'detail-search-next',
  53  |         items: [{ sourceKind: 'CARD', card: { id: card, organizationId: org, boardId: board, listId: list, title, version: 1, lifecycleState: url.searchParams.get('scope') === 'archived' ? 'archived' : 'active', dueAt: null, dueHasTime: false, dueComplete: false },
  54  |           boardName: 'Council operations', listName: 'To do', boardDateTimezone: null, labels: [], members: [], hasMoreLabels: false, hasMoreMembers: false }],
  55  |         interaction: { eventId: event, entityId: event, actorId: actor, version: 1, eventType: 'SEARCH_EXECUTED', entityType: 'Search', organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-21T12:00:00Z' },
  56  |       } });
  57  |     }
  58  |     return route.fulfill({ status: 503, json: {} });
  59  |   });
  60  |   return source;
  61  | }
  62  | const noDomainWrites = (paths: string[]) => expect(paths.filter(path => /^\/(boards|cards|lists)(\/|$)/.test(path))).toEqual([]);
  63  | async function fit(page: Page, width: number) {
  64  |   expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  65  | }
  66  | async function axe(page: Page) {
  67  |   expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  68  | }
  69  | 
  70  | for (const width of [1280, 768, 390, 320]) {
  71  |   test(`PRD-09 Trello Card context, dirty return and private withdrawal at ${width}px`, async ({ page }, info) => {
  72  |     await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page);
  73  |     await page.goto(`${boardPath}/cards/${card}`);
  74  |     const detail = page.getByRole('dialog', { name: 'Card details', exact: true });
  75  |     const location = detail.getByRole('region', { name: 'Card location', exact: true });
  76  |     const input = detail.getByRole('textbox', { name: 'Card title', exact: true });
  77  |     await expect(input).toBeEditable(); await expect(input).toHaveValue(title);
  78  |     if (polished) await expect(location).toHaveText('Council operations / To do');
  79  |     else await expect(location).toHaveCount(0);
  80  |     await expect(detail.getByRole('button', { name: 'Save card', exact: true })).toBeVisible();
  81  |     await expect(detail.getByRole('button', { name: 'Close', exact: true })).toBeVisible();
  82  |     await fit(page, width); await page.screenshot({ path: info.outputPath(`card-details-${width}.png`), fullPage: true });
  83  |     await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
  84  |     await fit(page, width); expect(await detail.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true); await axe(page);
  85  |     // A modal correctly removes the background Board from the accessible
  86  |     // surface. Its visible cover launcher is the current admission consumer.
  87  |     await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveCount(0);
  88  |     const coverReview = await disclosedWorkControl(page, 'Appearance actions', 'button', { name: 'Review Card cover', exact: true });
  89  |     await expect(coverReview).toBeEnabled();
  90  |     source.boardFailure = 503; source.holdBoard = true;
  91  |     await detail.getByRole('button', { name: 'Refresh card', exact: true }).press('Enter');
  92  |     await expect.poll(() => source.boardWaiters.length).toBeGreaterThan(0);
  93  |     await expect(location).toHaveCount(0);
  94  |     await expect(coverReview).toBeDisabled();
  95  |     source.holdBoard = false; source.boardWaiters.splice(0).forEach(resolve => resolve());
  96  |     await expect(detail.getByRole('alert')).toBeVisible(); await expect(location).toHaveCount(0);
  97  |     await expect(coverReview).toBeDisabled();
  98  |     source.boardFailure = 0; await detail.getByRole('button', { name: 'Refresh card', exact: true }).press('Enter');
  99  |     await expect(input).toBeEditable(); if (polished) await expect(location).toBeVisible();
  100 |     await expect(coverReview).toBeEnabled();
  101 |     await input.fill('Unsaved repair investigation');
  102 |     const guard = page.getByRole('dialog', { name: 'Discard unsaved changes?', exact: true });
  103 |     await openAdmittedReview(detail.getByRole('button', { name: 'Close', exact: true }), guard);
  104 |     await guard.getByRole('button', { name: 'Keep editing', exact: true }).press('Enter');
  105 |     await expect(input).toHaveValue('Unsaved repair investigation');
  106 |     await openAdmittedReview(detail.getByRole('button', { name: 'Close', exact: true }), guard);
  107 |     await guard.getByRole('button', { name: 'Discard changes and leave', exact: true }).press('Enter');
  108 |     await expect(detail).toHaveCount(0);
  109 |     const origin = page.getByRole('link', { name: title, exact: true }); await expect(origin).toBeFocused();
  110 |     await origin.press('Enter'); await expect(input).toHaveValue(title);
  111 |     source.boardFailure = 403; await detail.getByRole('button', { name: 'Refresh card', exact: true }).press('Enter');
  112 |     await expect(input).toHaveCount(0); await expect(location).toHaveCount(0); await expect(coverReview).toHaveCount(0); noDomainWrites(source.writes);
  113 |   });
  114 | 
  115 |   test(`PRD-16 Trello Search surfaces preserve drafts, paging and denial at ${width}px`, async ({ page }, info) => {
  116 |     await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page);
  117 |     await page.goto(`/app/${org}/search`);
  118 |     const query = page.getByRole('textbox', { name: 'Card text', exact: true });
  119 |     await query.fill('roof'); await query.press('Enter');
  120 |     const result = page.getByRole('link', { name: title, exact: true });
  121 |     await expect(result).toHaveAttribute('href', `${boardPath}/cards/${card}`);
  122 |     await expect(page.getByText('Council operations / To do', { exact: true })).toBeVisible();
  123 |     await expect(page.getByText('Labels: None', { exact: true })).toBeVisible();
  124 |     await expect(page.getByText('Members: None', { exact: true })).toBeVisible();
  125 |     await expect(page.getByText('No deadline', { exact: true })).toBeVisible();
  126 |     await expect(page.getByText('1 result on this page. More work can be searched.', { exact: true })).toBeVisible();
  127 |     await fit(page, width); await page.screenshot({ path: info.outputPath(`search-results-${width}.png`), fullPage: true });
  128 |     await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; }); await fit(page, width); await axe(page);
  129 |     const scope = page.getByRole('combobox', { name: 'Search scope', exact: true });
  130 |     await openAdmittedReview(scope, page.getByRole('option', { name: 'Archived work', exact: true }));
  131 |     await page.getByRole('option', { name: 'Archived work', exact: true }).press('Enter');
  132 |     await expect(scope).toHaveText('Archived work');
  133 |     await page.screenshot({ path: info.outputPath(`search-zoom-scope-${width}.png`), fullPage: true });
  134 |     await info.attach('selected-scope-layout', { contentType: 'application/json', body: JSON.stringify(await scope.evaluate(el => {
  135 |       const style = getComputedStyle(el), icon = el.parentElement!.querySelector('svg')!.getBoundingClientRect();
  136 |       const range = document.createRange(); range.selectNodeContents(el);
  137 |       return { classes: el.className, paddingRight: style.paddingRight, fontSize: style.fontSize,
  138 |         whiteSpace: style.whiteSpace, icon: icon.toJSON(), text: Array.from(range.getClientRects()).map(rect => rect.toJSON()) };
  139 |     })) });
  140 |     expect(await scope.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  141 |     expect(await scope.evaluate(el => {
  142 |       const icon = el.parentElement!.querySelector('svg')!.getBoundingClientRect();
  143 |       const range = document.createRange(); range.selectNodeContents(el);
  144 |       return Array.from(range.getClientRects()).every(rect => rect.right <= icon.left - 4 || rect.bottom <= icon.top || rect.top >= icon.bottom);
  145 |     })).toBe(true);
  146 |     await fit(page, width); await axe(page);
  147 |     await query.fill('unsent draft'); await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
  148 |     const feedback = page.getByRole('region', { name: 'Global Card search', exact: true }).getByRole('status');
  149 |     await expect(feedback).toBeFocused(); await expect(query).toHaveValue('unsent draft');
  150 |     expect(new URLSearchParams(source.searches.at(-1)).get('q')).toBe('roof');
> 151 |     expect(new URLSearchParams(source.searches.at(-1)).get('after')).toBe('detail-search-next');
      |                                                                      ^ Error: expect(received).toBe(expected) // Object.is equality
  152 |     expect(new URLSearchParams(source.searches.at(-1)).get('scope')).toBe('active');
  153 |     source.searchFailure = 503; await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
  154 |     await expect(page.getByText('Search is unavailable. Check your account and access, then search again.', { exact: true })).toBeVisible();
  155 |     await expect(feedback).toBeFocused(); await expect(query).toHaveValue('unsent draft'); await expect(result).toHaveCount(0);
  156 |     source.searchFailure = 0; await page.getByRole('button', { name: 'Search', exact: true }).press('Enter'); await expect(result).toBeVisible();
  157 |     expect(new URLSearchParams(source.searches.at(-1)).get('q')).toBe('unsent draft');
  158 |     source.searchFailure = 403; await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
  159 |     await expect(query).toHaveValue(''); await expect(result).toHaveCount(0); noDomainWrites(source.writes);
  160 |   });
  161 | }
  162 | 
```