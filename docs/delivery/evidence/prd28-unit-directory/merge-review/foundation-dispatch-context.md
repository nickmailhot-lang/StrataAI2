# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: board-background-pipeline.case.ts >> PRD-04 real public consent, publication recovery and image-backed copy at 1280px
- Location: tests/browser/board-background-pipeline.case.ts:60:7

# Error details

```
Error: expect(locator).toBeVisible() failed

Locator: getByText('Board background updated.', { exact: true })
Expected: visible
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeVisible" getByText('Board background updated.', { exact: true }) with timeout 5000ms
  - waiting for getByText('Board background updated.', { exact: true })

```

```yaml
- dialog "Card details":
  - heading "Card details" [level=2]
  - region "Card location":
    - paragraph: Owned image source / Source
  - heading "Summary" [level=3]
  - button "Edit Card labels" [disabled]
  - button "Edit Card assignees" [disabled]
  - button "Refresh card" [disabled]
  - text: Card title
  - textbox "Card title" [disabled]: Source image
  - text: Description
  - textbox "Description" [disabled]
  - paragraph: "Markdown: headings, bold, italic, links, lists, code and blockquotes."
  - region "Description preview":
    - heading "Description preview" [level=3]
    - paragraph: No description.
  - button "Show labels"
  - button "Show assignees"
  - region "Edit Card dates":
    - button "Edit dates" [disabled]
  - button "Move card" [disabled]
  - region "Personal due reminder":
    - button "Due reminder"
  - heading "Checklist and attachments" [level=3]
  - button "Show attachments"
  - button "Show checklists"
  - region "Create link attachment":
    - button "Add link attachment" [disabled]
  - region "Create file attachment":
    - button "Add file attachment" [disabled]
  - region "Manage Card attachments":
    - button "Manage attachments" [disabled]
  - region "Create checklist":
    - button "Add checklist" [disabled]
  - region "Manage checklists":
    - button "Manage checklists" [disabled]
  - group:
    - text: Appearance actions
    - region "Board background image":
      - button "Review Board background images" [disabled]
      - status: The background change is unconfirmed. Retry the original change to recover its acknowledgment.
      - paragraph: Private original.png
      - alert: This background image will be publicly visible, including to visitors who are not signed in.
      - checkbox "I understand this Board background image will be publicly visible" [checked] [disabled]
      - text: I understand this Board background image will be publicly visible
      - button "Retry original Board background change"
    - region "Card cover":
      - button "Review Card cover" [disabled]
      - status: Card cover removed.
  - button "Retention classification for this Card"
  - heading "Discussion" [level=3]
  - region "Card comments":
    - button "Review Card comments" [disabled]
  - heading "Activity history" [level=3]
  - button "Review Card activity"
  - group: Card actions
  - button "Save card" [disabled]
  - button "Close" [disabled]
- contentinfo "Application version": Version 0.1.0-29 · Build 019d43bf480414f29384d50be59850f631a6c7f9
```

# Test source

```ts
  142 |     for (const name of ['Save card', 'Manage attachments', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
  143 |     await pressAdmittedAction(coverRetry); await expect(page.getByText('Card cover updated.', { exact: true })).toBeVisible();
  144 |     expect(coverWrites).toHaveLength(2); expect(coverWrites[1]).toEqual(coverWrites[0]);
  145 |     expect(coverWrites[0].key).toMatch(/^[0-9a-f-]{36}$/i);
  146 |     expect(JSON.parse(coverWrites[0].body!)).toEqual({ attachmentId: candidate.attachmentId,
  147 |       attachmentVersion: candidate.attachmentVersion, cardVersion: initialCover.cardVersion, publicVisibilityConfirmed: true });
  148 |     const coverImage = page.getByRole('img', { name: 'Card cover', exact: true }); await expect(coverImage).toBeVisible();
  149 |     await expect.poll(() => coverImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  150 |     await expect(coverReview).toBeFocused();
  151 |     const selectedCoverResponse = await context.request.get(coverPath); expect(selectedCoverResponse.status()).toBe(200);
  152 |     expect(await selectedCoverResponse.json()).toMatchObject({ cardVersion: initialCover.cardVersion + 1,
  153 |       attachmentId: candidate.attachmentId, attachmentVersion: candidate.attachmentVersion });
  154 |     await expect.poll(admittedCardVersion).toBeGreaterThanOrEqual(initialCover.cardVersion + 1);
  155 |     const visitor = await browser.newContext({ baseURL: new URL(page.url()).origin });
  156 |     try {
  157 |       const publicCoverImage = await visitor.request.get(coverPath + '/image'); expect(publicCoverImage.status()).toBe(200);
  158 |       expect(publicCoverImage.headers()['content-type']).toBe('image/png');
  159 |       const coverBytes = await publicCoverImage.body(); expect(coverBytes.subarray(0, 8)).toEqual(Buffer.from('89504e470d0a1a0a', 'hex'));
  160 |       expect(coverBytes.includes(Buffer.from('PRIVATE ORIGINAL'))).toBe(false);
  161 |       expect((await visitor.request.get(coverPath)).status()).toBe(401);
  162 |     } finally { await visitor.close(); }
  163 |     // A distinct admitted Board member must retire already-rendered
  164 |     // disclosure through actual Worker delivery, without navigation or reload.
  165 |     const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
  166 |     try {
  167 |       expect((await peerContext.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' },
  168 |         data: { email: fixture.peer.email, password: fixture.peer.password } })).status()).toBe(200);
  169 |       const ownerProfile = await context.request.get('/me'); expect(ownerProfile.status()).toBe(200);
  170 |       const peerProfile = await peerContext.request.get('/me'); expect(peerProfile.status()).toBe(200);
  171 |       expect((await peerProfile.json()).id).not.toBe((await ownerProfile.json()).id);
  172 |       await waitForBoardDelivery(context.request, board);
  173 |       const peer = await peerContext.newPage(); let peerSequence = 0n;
  174 |       peer.on('websocket', socket => {
  175 |         if (new URL(socket.url()).pathname !== '/boards/live') return;
  176 |         socket.on('framereceived', frame => {
  177 |           for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
  178 |             const message = JSON.parse(raw);
  179 |             if (message.type !== 2 || !Array.isArray(message.item?.events)) continue;
  180 |             for (const event of message.item.events) {
  181 |               if (event.organizationId === org && event.boardId === board && typeof event.sequence === 'string'
  182 |                 && /^[0-9]{1,19}$/.test(event.sequence) && BigInt(event.sequence) > peerSequence) peerSequence = BigInt(event.sequence);
  183 |             }
  184 |           }
  185 |         });
  186 |       });
  187 |       await peer.goto(cardPath);
  188 |       const peerImage = peer.getByRole('img', { name: 'Card cover', exact: true }); await expect(peerImage).toBeVisible();
  189 |       await expect.poll(() => peerImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  190 |       await expect(peer.getByText('Live updates connected.', { exact: true })).toBeVisible();
  191 |       let peerNavigations = 0; peer.on('framenavigated', frame => { if (frame === peer.mainFrame()) peerNavigations++; });
  192 |       const priorSyncResponse = await context.request.get(`/boards/${board}/sync`); expect(priorSyncResponse.status()).toBe(200);
  193 |       const priorSync = await priorSyncResponse.json(); expect(priorSync).toMatchObject({ hasMore: false, pending: false, resetRequired: false });
  194 |       expect(priorSync.cursor).toMatch(/^[0-9]{1,19}$/);
  195 |       // Return from the peer window through a current owner snapshot before
  196 |       // reviewing an unsent removal. A foreground access read must still
  197 |       // refuse a cover review that overlaps it; never replay that failed read
  198 |       // or weaken the exact cover/receipt and peer no-navigation assertions.
  199 |       await page.bringToFront();
  200 |       const priorOwnerReads = reads();
  201 |       await pressObservedLocalAction(page.getByRole('button', { name: 'Refresh card', exact: true }));
  202 |       await expect.poll(reads).toBeGreaterThan(priorOwnerReads);
  203 |       await expect(coverReview).toBeEnabled();
  204 |       await openCoverReview();
  205 |       await pressAdmittedAction(page.getByRole('button', { name: 'Remove Card cover', exact: true }));
  206 |       await expect(coverConsent).toHaveCount(0);
  207 |       await pressAdmittedAction(page.getByRole('button', { name: 'Confirm cover removal', exact: true }));
  208 |       await expect(page.getByText('Card cover removed.', { exact: true })).toBeVisible(); await expect(coverImage).toHaveCount(0);
  209 |       await expect(coverReview).toBeFocused(); expect(coverWrites).toHaveLength(3);
  210 |       const removedCoverResponse = await context.request.get(coverPath); expect(removedCoverResponse.status()).toBe(200);
  211 |       expect(await removedCoverResponse.json()).toMatchObject({ cardVersion: initialCover.cardVersion + 2, attachmentId: null });
  212 |       await expect.poll(admittedCardVersion).toBeGreaterThanOrEqual(initialCover.cardVersion + 2);
  213 |       await waitForBoardDelivery(context.request, board);
  214 |       await expect.poll(() => peerSequence > BigInt(priorSync.cursor), { timeout: 20_000 }).toBe(true);
  215 |       await expect(peerImage).toHaveCount(0, { timeout: 20_000 }); await expect(peer).toHaveURL(new RegExp(`/cards/${fixture.cardId}$`));
  216 |       expect(peerNavigations).toBe(0);
  217 |       const peerCoverResponse = await peerContext.request.get(coverPath); expect(peerCoverResponse.status()).toBe(200);
  218 |       expect(await peerCoverResponse.json()).toMatchObject({ cardVersion: initialCover.cardVersion + 2, attachmentId: null });
  219 |       expect((await peerContext.request.get(coverPath + '/image')).status()).toBe(404);
  220 |       expect((await context.request.get(coverPath + '/image')).status()).toBe(404);
  221 |     } finally { await peerContext.close(); }
  222 |     await page.unroute('**' + coverPath);
  223 |     const attempts: { key: string | undefined; body: string | null }[] = [];
  224 |     await page.route(`**/boards/${board}/background/image`, async route => {
  225 |       expect(route.request().method()).toBe('POST');
  226 |       const result = await route.fetch(); expect(result.status()).toBe(200);
  227 |       attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
  228 |       if (attempts.length === 1) await route.abort('failed');
  229 |       else { expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]); await route.fulfill({ response: result }); }
  230 |     });
  231 |     const review = (await disclosedWorkControl(page, 'Appearance actions', 'button', { name: 'Review Board background images', exact: true }));
  232 |     await openAdmittedReview(review, page.getByRole('button', { name: 'Use Private original.png as Board background', exact: true }));
  233 |     await pressAdmittedAction(page.getByRole('button', { name: 'Use Private original.png as Board background', exact: true }));
  234 |     const confirm = page.getByRole('button', { name: 'Confirm Board background image', exact: true });
  235 |     await expect(confirm).toBeDisabled();
  236 |     const consent = page.getByRole('checkbox', { name: 'I understand this Board background image will be publicly visible', exact: true });
  237 |     await expect(consent).toBeFocused(); await pressAdmittedAction(consent, 'Space');
  238 |     await pressAdmittedAction(confirm);
  239 |     const retry = page.getByRole('button', { name: 'Retry original Board background change', exact: true });
  240 |     await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
  241 |     for (const name of ['Save card', 'Review Card cover', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
> 242 |     await pressAdmittedAction(retry); await expect(page.getByText('Board background updated.', { exact: true })).toBeVisible();
      |                                                                                                                  ^ Error: expect(locator).toBeVisible() failed
  243 |     await expect(review).toBeFocused(); expect(attempts).toHaveLength(2);
  244 |     expect(JSON.parse(attempts[0].body!)).toMatchObject({ publicVisibilityConfirmed: true, boardVersion: before.version + 1 });
  245 |     const sourceResponse = await context.request.get(`/boards/${board}`); expect(sourceResponse.status()).toBe(200);
  246 |     const source = (await sourceResponse.json()).board;
  247 |     expect(source.backgroundType).toBe('IMAGE'); expect(source.backgroundValue).toMatch(/^[0-9a-f-]{36}$/);
  248 |     const image = page.locator(`img[src="/boards/${board}/background/image?boardVersion=${source.version}"]`);
  249 |     await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  250 |     await expect(image).toHaveAttribute('alt', '');
  251 |     expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  252 |     // Wait for native actionability through the current Card/Board read. A
  253 |     // keypress can target a control that became disabled since an assertion.
  254 |     const boardPath = `/app/${org}/boards/${board}`;
  255 |     const closedDetailReads = trackBoardReads(page, board, boardPath);
  256 |     await page.getByRole('button', { name: 'Close', exact: true }).click();
  257 |     await expect(page.getByRole('dialog')).toHaveCount(0);
  258 |     // Closing detail only changes the route. Explicitly refresh its Board so
  259 |     // readiness is based on a completed read, rather than an assumed request.
  260 |     await page.getByRole('button', { name: 'Refresh board', exact: true }).click();
  261 |     await expect.poll(closedDetailReads).toBeGreaterThanOrEqual(1);
  262 |     await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
  263 |     const copy = await disclosedWorkControl(page, 'Board actions', 'button', { name: 'Copy Board', exact: true });
  264 |     await openAdmittedReview(copy, page.getByRole('dialog', { name: 'Copy Board', exact: true }));
  265 |     await expect(page.getByRole('dialog', { name: 'Copy Board', exact: true })).toBeVisible();
  266 |     let copyWrites = 0;
  267 |     page.on('request', request => { if (request.method() === 'POST'
  268 |       && new URL(request.url()).pathname === `/boards/${board}/copy`) copyWrites++; });
  269 |     const copyResponse = page.waitForResponse(response => response.request().method() === 'POST'
  270 |       && new URL(response.url()).pathname === `/boards/${board}/copy`);
  271 |     // A foreground read can invalidate confirmation between actionability and
  272 |     // activation. Observe the one POST and stop activating as soon as it starts.
  273 |     const create = page.getByRole('button', { name: 'Create Board copy', exact: true });
  274 |     await pressObservedRequest(create, () => copyWrites, 1);
  275 |     const copyReceipt = await copyResponse; expect(copyReceipt.status()).toBe(201); expect(copyWrites).toBe(1);
  276 |     const acknowledgedCopy = await copyReceipt.json();
  277 |     const open = page.getByRole('link', { name: 'Open copied Board', exact: true }); await expect(open).toBeVisible(); await expect(open).toBeFocused();
  278 |     const href = await open.getAttribute('href'); const target = href!.split('/').at(-1)!; expect(target).not.toBe(board);
  279 |     expect(acknowledgedCopy).toMatchObject({ id: target, organizationId: org, visibility: 'PRIVATE', version: 1, backgroundType: 'IMAGE' });
  280 |     await waitForBoardDelivery(context.request, target);
  281 |     const copiedResponse = await context.request.get(`/boards/${target}`); expect(copiedResponse.status()).toBe(200);
  282 |     const copied = (await copiedResponse.json()).board;
  283 |     expect(copied).toMatchObject({ visibility: 'PRIVATE', backgroundType: 'IMAGE', version: 1 });
  284 |     expect(copied.backgroundValue).not.toBe(source.backgroundValue);
  285 |     await open.press('Enter'); await expect(page).toHaveURL(new RegExp(`/boards/${target}$`));
  286 |     const copiedImage = page.locator(`img[src="/boards/${target}/background/image?boardVersion=1"]`);
  287 |     await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  288 |     expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  289 |     expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
  290 |     // Retire the source selection through the actual command, then discard all
  291 |     // browser state. The destination must still admit its independent owner.
  292 |     const cleared = await context.request.patch(`/boards/${board}`, {
  293 |       headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
  294 |       data: { name: source.name, version: source.version, backgroundType: 'COLOR', backgroundValue: null },
  295 |     });
  296 |     expect(cleared.status()).toBe(200);
  297 |     expect(await cleared.json()).toMatchObject({ version: source.version + 1, backgroundType: 'COLOR', backgroundValue: null });
  298 |     expect((await context.request.get(`/boards/${board}/background/image`)).status()).toBe(404);
  299 |     const copiedPath = `/app/${org}/boards/${target}`;
  300 |     const freshReads = trackBoardReads(page, target, copiedPath);
  301 |     await page.reload(); await expect.poll(freshReads).toBeGreaterThanOrEqual(2);
  302 |     await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  303 |     await expect(await disclosedWorkControl(page, 'Board actions', 'button', { name: 'Copy Board', exact: true })).toBeEnabled();
  304 |     const retained = await context.request.get(`/boards/${target}`);
  305 |     expect(retained.status()).toBe(200);
  306 |     expect((await retained.json()).board).toMatchObject({ version: 1, backgroundType: 'IMAGE', backgroundValue: copied.backgroundValue });
  307 |     // Restore the source visibility for the next independent viewport case
  308 |     // and the subsequent private HTTP lifecycle/concurrency assertions.
  309 |     const privateVisibility = await context.request.patch(`/boards/${board}/visibility`, {
  310 |       headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
  311 |       data: { visibility: 'PRIVATE', version: source.version + 1 },
  312 |     });
  313 |     expect(privateVisibility.status()).toBe(200);
  314 |     // The destination is still open with genuine private image bytes. A real
  315 |     // session withdrawal must retire rendered disclosure without a reload.
  316 |     await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
  317 |     expect((await context.request.post('/auth/logout', {
  318 |       headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() }, data: {},
  319 |     })).status()).toBe(204);
  320 |     await expect(copiedImage).toHaveCount(0, { timeout: 10_000 });
  321 |     await expect(page.getByRole('button', { name: 'Copy Board', exact: true })).toHaveCount(0);
  322 |     expect((await context.request.get(`/boards/${target}/background/image`)).status()).toBe(404);
  323 |     expect((await context.request.post('/auth/login', {
  324 |       headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
  325 |       data: { email: fixture.email, password: fixture.password },
  326 |     })).status()).toBe(200);
  327 |     const readmitted = await context.request.get(`/boards/${target}`); expect(readmitted.status()).toBe(200);
  328 |     expect((await readmitted.json()).board).toEqual(copied);
  329 |     const admittedReads = trackBoardReads(page, target, copiedPath);
  330 |     await page.goto(copiedPath); await expect.poll(admittedReads).toBeGreaterThanOrEqual(2);
  331 |     await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  332 |   });
  333 | }
  334 | 
```