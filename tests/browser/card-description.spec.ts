import AxeBuilder from '@axe-core/playwright';
import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { pressAdmittedAction } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, waitForBoardReads } from './boardReadTracker';
import { armCachedDetailPaint, readCachedDetailPaint } from './cachedDetailPaint';

for (const width of [1280, 390]) {
  test(`PRD-09 FR-008/009, AC-DETAIL-09-03, TC-01/05/10/11/12: safe Markdown original, live peer, keyboard editing and archived/session withdrawal at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `description-${width}-${Date.now()}@example.test`, password: 'description-correct-horse-battery', displayName: 'Description fixture' };
    await registerNotificationAccount(context.request, credentials);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Description scope' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Description Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const createdList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Description List' } });
    expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
    const original = '# Review scope\n\n**Strong text** and *emphasis* with [Manual](https://example.test/manual).\n\n- First item\n- Second item\n\n3. Third item\n4. Fourth item\n\n> Retained quote\n\n```html\n<script>literal code</script>\n```\n\n<script>globalThis.descriptionAttack = true</script>\n\n![Diagram alternative](https://description-probe.example.test/pixel)\n\n<img src="https://description-probe.example.test/raw" onerror="globalThis.descriptionAttack = true">\n\n[Unsafe](javascript:alert(1))';
    const created = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Markdown Card', description: original } });
    expect(created.status()).toBe(201); const card = (await created.json()).id;
    const restoreWorker = scopedBoardWorker(org);
    const peer = await browser.newContext({ viewport: { width, height: 844 } });
    let externalRequests = 0;
    await page.route('https://description-probe.example.test/**', async route => { ++externalRequests; await route.fulfill({ status: 403, body: '' }); });
    try {
      await waitForBoardDelivery(context.request, board);
      const path = `/app/${org}/boards/${board}/cards/${card}`;
      expect((await peer.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      const other = await peer.newPage();
      await other.route('https://description-probe.example.test/**', async route => { ++externalRequests; await route.fulfill({ status: 403, body: '' }); });
      await other.goto(path);
      const otherPreview = other.getByRole('region', { name: 'Description preview', exact: true });
      await expect(otherPreview.getByRole('heading', { name: 'Review scope', exact: true })).toBeVisible();
      await page.bringToFront();
      const boardPath = `/app/${org}/boards/${board}`;
      const reads = trackBoardReads(page, board, boardPath);
      await page.goto(boardPath); await waitForBoardReads(page, reads, 2);
      expect(reads()).toBeGreaterThanOrEqual(2);
      const cardLink = page.getByRole('link', { name: 'Markdown Card', exact: true });
      await expect(page.getByRole('button', { name: 'Drag Markdown Card card', exact: true })).toBeEnabled();
      await page.evaluate(armCachedDetailPaint, { path, title: 'Markdown Card', richText: { source: original, heading: 'Review scope' } });
      const openingBegan = performance.now(); await cardLink.click();
      const clickMs = performance.now() - openingBegan;
      await expect.poll(() => page.evaluate(expected => {
        const dialog = document.querySelector('[role="dialog"]');
        const title = dialog?.querySelector<HTMLInputElement>('input[name="title"]');
        const source = dialog?.querySelector<HTMLTextAreaElement>('textarea[name="description"]');
        const previewHeading = dialog?.querySelector('[data-description-content] h3');
        return !!dialog && dialog.getClientRects().length > 0
          && !dialog.closest('[inert],[aria-hidden="true"]')
          && !!title && !title.disabled && !!source && !source.disabled
          && source.value === expected && previewHeading?.textContent === 'Review scope';
      }, original), { intervals: [10], timeout: 5_000 }).toBe(true);
      const detailAutomationMs = performance.now() - openingBegan;
      const openingMs = await page.evaluate(readCachedDetailPaint);
      const detail = page.getByRole('dialog', { name: 'Card details', exact: true });
      await expect(detail.getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
      const description = detail.getByRole('textbox', { name: 'Description', exact: true });
      await expect(description).toHaveValue(original);
      await test.info().attach('card-description-performance.json', { contentType: 'application/json', body: JSON.stringify({
        fixture: { lists: 1, cards: 1, descriptionCharacters: original.length, width, assets: 'warm', initialAdmissionSettled: reads() >= 2 },
        topology: /^[0-9a-f]{40,64}$/.test(process.env.GITHUB_SHA ?? '') ? 'exact release images through Nginx' : 'unverified runtime',
        cachedDetailMs: openingMs, detailAutomationMs, clickMs,
        measurement: 'browser-painted-rich-detail',
        readiness: 'intended native click to exact focused editable title/source and full preview, stable through the following painted frame', budgetMs: 200,
      }) });
      expect(openingMs).toBeLessThan(200);
      const preview = detail.getByRole('region', { name: 'Description preview', exact: true });
      await expect(preview.getByRole('heading', { name: 'Review scope', exact: true })).toBeVisible();
      await expect(preview.locator('strong')).toHaveText('Strong text');
      await expect(preview.locator('em')).toHaveText('emphasis');
      await expect(preview.locator('li')).toHaveCount(4);
      await expect(preview.locator('blockquote')).toHaveText('Retained quote');
      await expect(preview.locator('pre code')).toHaveText('<script>literal code</script>\n');
      await expect(preview.getByRole('link', { name: 'Manual', exact: true })).toHaveAttribute('rel', 'noopener noreferrer nofollow');
      await expect(preview.getByRole('link', { name: 'Manual', exact: true })).toHaveAttribute('title', 'Opens in a new tab.');
      await expect(preview.getByText('(new tab)', { exact: true })).toBeVisible();
      await expect(preview.getByRole('link', { name: 'Unsafe', exact: true })).toHaveCount(0);
      await expect(preview.locator('img,script,iframe,svg')).toHaveCount(0);
      await expect(preview.getByText('Diagram alternative', { exact: true })).toBeVisible();
      expect(await page.evaluate(() => (globalThis as typeof globalThis & { descriptionAttack?: boolean }).descriptionAttack)).toBeUndefined();
      expect(externalRequests).toBe(0);
      const updated = original.replace('# Review scope', '# Updated review');
      await description.focus(); await description.fill(updated);
      await expect(preview.getByRole('heading', { name: 'Updated review', exact: true })).toBeVisible();
      await expect(description).toBeFocused();
      await pressAdmittedAction(detail.getByRole('button', { name: 'Save card', exact: true }));
      await expect(detail.getByText('Changes saved.', { exact: true })).toBeVisible();
      const persisted = await context.request.get(`/boards/${board}`); expect(persisted.status()).toBe(200);
      const snapshot = await persisted.json();
      const stored = snapshot.lists.flatMap((column: { cards: { id: string; description: string; version: number }[] }) => column.cards).find((value: { id: string }) => value.id === card);
      expect(stored).toMatchObject({ description: updated, version: 2 });
      // A separately authenticated session receives the original Worker change
      // through its live Board subscription, without navigation or reload.
      await expect(otherPreview.getByRole('heading', { name: 'Updated review', exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(other.getByRole('textbox', { name: 'Description', exact: true })).toHaveValue(updated);
      await expect(otherPreview.locator('img,script,iframe,svg')).toHaveCount(0);
      await expect(otherPreview.getByRole('link', { name: 'Unsafe', exact: true })).toHaveCount(0);
      expect(await other.evaluate(() => (globalThis as typeof globalThis & { descriptionAttack?: boolean }).descriptionAttack)).toBeUndefined();
      await page.reload(); await expect(description).toHaveValue(updated);
      await expect(preview.getByRole('heading', { name: 'Updated review', exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const archived = await context.request.post(`/cards/${card}/archive`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { version: 2 } });
      expect(archived.status()).toBe(200); await waitForBoardDelivery(context.request, board);
      await page.goto(path);
      await expect(preview.getByRole('heading', { name: 'Updated review', exact: true })).toBeVisible();
      await expect(detail.getByRole('textbox', { name: 'Description', exact: true })).toHaveCount(0);
      const retained = await context.request.get(`/boards/${board}/cards/${card}/archived-details`);
      expect(retained.status()).toBe(200); expect(await retained.json()).toMatchObject({ description: updated });
      expect(externalRequests).toBe(0);
      expect((await context.request.post('/auth/logout', { headers, data: {} })).status()).toBe(204);
      await expect(page).toHaveURL(/\/login$/, { timeout: 20_000 });
      await expect(page.getByRole('region', { name: 'Description preview', exact: true })).toHaveCount(0);
      await expect(page.getByText('Updated review', { exact: true })).toHaveCount(0);
      expect((await context.request.get(`/boards/${board}/cards/${card}/archived-details`)).status()).toBe(401);
      expect((await peer.request.get(`/boards/${board}/cards/${card}/archived-details`)).status()).toBe(200);
    } finally { await peer.close(); restoreWorker(); }
  });
}

for (const width of [1280, 390]) for (const role of ['MEMBER', 'ADMIN'] as const) {
  test(`PRD-09 TC-04/05/08/11/12: distinct ${role} edits and unsaved draft withdrawal at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const owner = await registerNotificationAccount(context.request, {
      email: `description-editor-owner-${width}-${role}-${Date.now()}@example.test`,
      password: 'description-editor-owner-correct-horse', displayName: 'Editor fixture owner',
    });
    const editor = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      const credentials = { email: `description-editor-${width}-${role}-${Date.now()}@example.test`,
        password: 'description-editor-correct-horse', displayName: 'Distinct description editor' };
      const actor = await registerNotificationAccount(editor.request, credentials);
      expect(actor.user.id).not.toBe(owner.user.id);
      const organization = await context.request.post('/organizations', { headers, data: { name: 'Description editor roles' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, {
        headers, data: { email: credentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' },
      });
      expect(invitation.status()).toBe(201);
      expect((await editor.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Editing role Board', visibility: 'ORGANIZATION' } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
      const granted = await context.request.patch(`/boards/${board}/members/${actor.user.id}`, { headers, data: { role } });
      expect(granted.status()).toBe(200); const grant = await granted.json(); expect(grant).toMatchObject({ userId: actor.user.id, role, active: true });
      const createdList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Editor List' } });
      expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
      const codeLine = 'long literal segment '.repeat(12);
      const original = '# Editor role source\n\n**Original emphasis** and [Reference](https://example.test/editor).\n\n```text\n' + codeLine + '\n```\n\n<script>globalThis.descriptionAttack = true</script>\n\n[Unsafe](javascript:alert(1))\n\n![Inert original](https://description-probe.example.test/editor)';
      const createdCard = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Role-editable Card', description: original } });
      expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const path = `/app/${org}/boards/${board}/cards/${card}`;
      const editorPage = await editor.newPage(); let documents = 0; let externalRequests = 0;
      editorPage.on('request', request => { if (request.isNavigationRequest() && request.frame() === editorPage.mainFrame()) documents++; });
      for (const target of [page, editorPage]) await target.route('https://description-probe.example.test/**', async route => {
        externalRequests++; await route.fulfill({ status: 403, body: '' });
      });
      await page.goto(path);
      const ownerPreview = page.getByRole('region', { name: 'Description preview', exact: true });
      await expect(ownerPreview.getByRole('heading', { name: 'Editor role source', exact: true })).toBeVisible();
      await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
      const reads = trackBoardReads(editorPage, board, path);
      await editorPage.goto(path); await waitForBoardReads(editorPage, reads, 2);
      const admission = await editor.request.get(`/boards/${board}`); expect(admission.status()).toBe(200);
      expect((await admission.json()).access).toMatchObject({ canView: true, canEdit: true, canAdminister: role === 'ADMIN' });
      const detail = editorPage.getByRole('dialog'); const description = detail.getByRole('textbox', { name: 'Description', exact: true });
      const preview = detail.getByRole('region', { name: 'Description preview', exact: true });
      await expect(description).toBeEnabled(); await expect(description).toHaveValue(original);
      const savedDescription = original.replace('# Editor role source', '# Saved by distinct editor');
      await detail.getByRole('textbox', { name: 'Card title', exact: true }).fill('Role-edited Card');
      await description.focus(); await description.fill(savedDescription);
      await expect(description).toBeFocused();
      await pressAdmittedAction(detail.getByRole('button', { name: 'Save card', exact: true }));
      await expect(detail.getByText('Changes saved.', { exact: true })).toBeVisible();
      await waitForBoardDelivery(context.request, board);
      await expect(ownerPreview.getByRole('heading', { name: 'Saved by distinct editor', exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toHaveValue(savedDescription);
      const accepted = await context.request.get(`/boards/${board}`); expect(accepted.status()).toBe(200); const snapshot = await accepted.json();
      expect(snapshot.lists.flatMap((column: { cards: { id: string }[] }) => column.cards).find((row: { id: string }) => row.id === card))
        .toMatchObject({ id: card, title: 'Role-edited Card', description: savedDescription, version: 2 });
      const sync = await context.request.get(`/boards/${board}/sync`); expect(sync.status()).toBe(200);
      const cardUpdates = (await sync.json()).events.filter((event: { eventType: string; entityId: string }) => event.eventType === 'CARD_UPDATED' && event.entityId === card);
      expect(cardUpdates).toHaveLength(1); expect(cardUpdates[0]).toMatchObject({ actorId: actor.user.id, version: 2 });
      const draft = savedDescription.replace('# Saved by distinct editor', '# Unsaved after grant removal');
      await description.focus(); await description.fill(draft);
      await expect(preview.getByRole('heading', { name: 'Unsaved after grant removal', exact: true })).toBeVisible();
      const membershipBefore = await context.request.get(`/organizations/${org}/members/${actor.user.id}`); expect(membershipBefore.status()).toBe(200);
      const organizationMembership = await membershipBefore.json();
      const removed = await context.request.delete(`/boards/${board}/members/${actor.user.id}`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID(), 'If-Match': `"${grant.version}"` },
      });
      expect(removed.status()).toBe(204); await waitForBoardDelivery(context.request, board);
      await expect(description).toBeDisabled({ timeout: 20_000 });
      await expect(detail.getByRole('button', { name: 'Save card', exact: true })).toHaveCount(0);
      await expect(detail.getByText('Editing is unavailable. Your unsaved changes are shown here and have not been saved.', { exact: true })).toBeVisible();
      await expect(description).toHaveValue(draft);
      await expect(ownerPreview.getByRole('heading', { name: 'Unsaved after grant removal', exact: true })).toHaveCount(0);
      const readOnly = await editor.request.get(`/boards/${board}`); expect(readOnly.status()).toBe(200);
      expect((await readOnly.json()).access).toMatchObject({ canView: true, canEdit: false, canAdminister: false });
      const refused = await editor.request.patch(`/cards/${card}`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Refused role edit', description: draft, version: 2 },
      });
      expect(refused.status()).toBe(404); expect((await refused.json()).code).toBe('card_not_found');
      const unchanged = await context.request.get(`/boards/${board}`); expect(unchanged.status()).toBe(200);
      expect((await unchanged.json()).lists).toEqual(snapshot.lists);
      const membershipAfter = await context.request.get(`/organizations/${org}/members/${actor.user.id}`); expect(membershipAfter.status()).toBe(200);
      expect(await membershipAfter.json()).toEqual(organizationMembership);
      const afterSync = await context.request.get(`/boards/${board}/sync`); expect(afterSync.status()).toBe(200);
      expect((await afterSync.json()).events.filter((event: { eventType: string; entityId: string }) => event.eventType === 'CARD_UPDATED' && event.entityId === card)).toEqual(cardUpdates);
      await expect(preview.locator('img,script,iframe,svg')).toHaveCount(0);
      await expect(preview.getByRole('link', { name: 'Unsafe', exact: true })).toHaveCount(0);
      const code = preview.locator('pre');
      expect(await code.textContent()).toBe(codeLine + '\n');
      expect(await code.evaluate(node => node.scrollWidth > node.clientWidth)).toBe(true);
      await preview.getByRole('link', { name: 'Reference', exact: true }).focus();
      await editorPage.keyboard.press('Tab'); await expect(code).toBeFocused();
      expect(await code.evaluate(node => Number.parseFloat(getComputedStyle(node).outlineWidth))).toBeGreaterThan(0);
      await editorPage.keyboard.press('ArrowRight');
      await expect.poll(() => code.evaluate(node => node.scrollLeft)).toBeGreaterThan(0);
      await editorPage.keyboard.press('ArrowLeft');
      await expect.poll(() => code.evaluate(node => node.scrollLeft)).toBe(0);
      expect(await editorPage.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect(await editorPage.evaluate(() => (globalThis as typeof globalThis & { descriptionAttack?: boolean }).descriptionAttack)).toBeUndefined();
      expect((await new AxeBuilder({ page: editorPage }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const privateBoard = await context.request.patch(`/boards/${board}/visibility`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { visibility: 'PRIVATE', version: snapshot.board.version },
      });
      expect(privateBoard.status()).toBe(200); await waitForBoardDelivery(context.request, board);
      await expect(preview).toHaveCount(0, { timeout: 20_000 });
      await expect(editorPage.getByText('Unsaved after grant removal', { exact: true })).toHaveCount(0);
      await expect(editorPage.getByRole('textbox', { name: 'Description', exact: true })).toHaveCount(0);
      await expect(editorPage.getByText('Editing is unavailable. Your unsaved changes are shown here and have not been saved.', { exact: true })).toHaveCount(0);
      expect((await editor.request.get(`/boards/${board}`)).status()).toBe(404);
      expect((await editor.request.get('/me')).status()).toBe(200);
      expect(documents).toBe(1); expect(externalRequests).toBe(0);
    } finally { restoreWorker(); await editor.close(); }
  });
}

for (const width of [1280, 390]) for (const audience of ['organization-reader', 'anonymous'] as const) {
  test(`PRD-09 TC-04/05/08/11/12: read-only Markdown, live revision and visibility withdrawal for ${audience} at ${width}px`, async ({ context, browser }) => {
    test.setTimeout(120_000);
    const headers = { 'X-StrataAI-Request': '1' };
    const ownerCredentials = { email: `description-role-owner-${width}-${audience}-${Date.now()}@example.test`,
      password: 'description-role-correct-horse', displayName: 'Description owner' };
    const owner = await registerNotificationAccount(context.request, ownerCredentials);
    const client = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      const organization = await context.request.post('/organizations', { headers, data: { name: 'Description role scope' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      if (audience === 'organization-reader') {
        const credentials = { email: `description-reader-${width}-${Date.now()}@example.test`,
          password: 'description-reader-correct-horse', displayName: 'Description reader' };
        const recipient = await registerNotificationAccount(client.request, credentials);
        expect(recipient.user.id).not.toBe(owner.user.id);
        const invitation = await context.request.post(`/organizations/${org}/invitations`, {
          headers, data: { email: credentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' },
        });
        expect(invitation.status()).toBe(201);
        expect((await client.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      } else expect((await client.request.get('/me')).status()).toBe(401);
      const createdBoard = await context.request.post('/boards', { headers, data: {
        organizationId: org, name: 'Read-only descriptions', visibility: audience === 'anonymous' ? 'PUBLIC' : 'ORGANIZATION',
      } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
      const createdList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Read-only List' } });
      expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
      const original = '# Authorized description\n\n**Read-only strong** and *read-only emphasis*.\n\n[Reference](https://example.test/reference)\n\n- Visible item\n\n> Visible quote\n\n`literal code`\n\n<script>globalThis.descriptionAttack = true</script>\n\n[Unsafe](javascript:alert(1))\n\n![Inert alternative](https://description-probe.example.test/reader)';
      const createdCard = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Read-only Markdown Card', description: original } });
      expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const admission = await client.request.get(`/boards/${board}`); expect(admission.status()).toBe(200);
      expect((await admission.json()).access).toMatchObject({ canView: true, canEdit: false, canAdminister: false });
      const page = await client.newPage(); let externalRequests = 0; let documents = 0;
      page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      await page.route('https://description-probe.example.test/**', async route => { externalRequests++; await route.fulfill({ status: 403, body: '' }); });
      const path = `/app/${org}/boards/${board}/cards/${card}`; const reads = trackBoardReads(page, board, path);
      // This delivered fixture has an initial snapshot, a first-head read, and
      // the coalesced read for the head's existing Card events. The second read
      // alone can leave that third refresh queued across the keyboard check.
      await page.goto(path); await waitForBoardReads(page, reads, 3);
      const preview = page.getByRole('region', { name: 'Description preview', exact: true });
      await expect(preview.getByRole('heading', { name: 'Authorized description', exact: true })).toBeVisible();
      await expect(preview.locator('strong')).toHaveText('Read-only strong');
      await expect(preview.locator('em')).toHaveText('read-only emphasis');
      const reference = preview.getByRole('link', { name: 'Reference', exact: true });
      await expect(reference).toHaveAttribute('href', 'https://example.test/reference');
      await expect(reference).toHaveAttribute('title', 'Opens in a new tab.');
      await expect(preview.getByText('(new tab)', { exact: true })).toBeVisible();
      // Foreground admission can replace the next disclosure's Tab stop.
      // Settle that read before checking an uninterrupted keyboard round trip.
      await page.bringToFront();
      await expect.poll(() => page.evaluate(() => document.hasFocus())).toBe(true);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      await reference.focus(); await expect(reference).toBeFocused();
      await page.keyboard.press('Tab');
      await expect(page.getByRole('button', { name: 'Show labels', exact: true })).toBeFocused();
      await page.keyboard.press('Shift+Tab');
      await expect(reference).toBeFocused();
      expect(await reference.evaluate(node => Number.parseFloat(getComputedStyle(node).outlineWidth))).toBeGreaterThan(0);
      await expect(preview.getByRole('link', { name: 'Unsafe', exact: true })).toHaveCount(0);
      await expect(preview.locator('img,script,iframe,svg')).toHaveCount(0);
      await expect(preview.getByText('Inert alternative', { exact: true })).toBeVisible();
      await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Save card', exact: true })).toHaveCount(0);
      expect(await page.evaluate(() => (globalThis as typeof globalThis & { descriptionAttack?: boolean }).descriptionAttack)).toBeUndefined();
      expect(externalRequests).toBe(0);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const updated = original.replace('# Authorized description', '# Current authorized description');
      const saved = await context.request.patch(`/cards/${card}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { title: 'Read-only Markdown Card', description: updated, version: 1 } });
      expect(saved.status()).toBe(200); expect(await saved.json()).toMatchObject({ id: card, description: updated, version: 2 });
      await waitForBoardDelivery(context.request, board);
      await expect(preview.getByRole('heading', { name: 'Current authorized description', exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toHaveValue(updated);
      const before = await context.request.get(`/boards/${board}`); expect(before.status()).toBe(200); const snapshot = await before.json();
      const refused = await client.request.patch(`/cards/${card}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { title: 'Unauthorized change', description: 'Unauthorized source', version: 2 } });
      expect(refused.status()).toBe(audience === 'anonymous' ? 401 : 404);
      if (audience === 'organization-reader') expect((await refused.json()).code).toBe('card_not_found');
      else expect(await refused.text()).not.toContain('Current authorized description');
      const unchanged = await context.request.get(`/boards/${board}`); expect(unchanged.status()).toBe(200);
      expect((await unchanged.json()).lists).toEqual(snapshot.lists);
      const withdrawal = await context.request.patch(`/boards/${board}/visibility`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { visibility: 'PRIVATE', version: snapshot.board.version },
      });
      expect(withdrawal.status()).toBe(200); await waitForBoardDelivery(context.request, board);
      await expect(preview).toHaveCount(0, { timeout: 20_000 });
      await expect(page.getByText('Current authorized description', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toHaveCount(0);
      const denied = await client.request.get(`/boards/${board}`); expect(denied.status()).toBe(404);
      expect(await denied.text()).not.toContain('Current authorized description');
      await expect(page).not.toHaveURL(/\/login$/);
      expect((await client.request.get('/me')).status()).toBe(audience === 'anonymous' ? 401 : 200);
      const retained = await context.request.get(`/boards/${board}`); expect(retained.status()).toBe(200);
      expect((await retained.json()).lists).toEqual(snapshot.lists);
      expect(documents).toBe(1); expect(externalRequests).toBe(0);
    } finally { restoreWorker(); await client.close(); }
  });
}
