import AxeBuilder from '@axe-core/playwright';
import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { pressAdmittedAction } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, waitForBoardReads } from './boardReadTracker';

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
      const openingMs = performance.now() - openingBegan;
      const detail = page.getByRole('dialog', { name: 'Card details', exact: true });
      await expect(detail.getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
      const description = detail.getByRole('textbox', { name: 'Description', exact: true });
      await expect(description).toHaveValue(original);
      await test.info().attach('card-description-performance.json', { contentType: 'application/json', body: JSON.stringify({
        fixture: { lists: 1, cards: 1, descriptionCharacters: original.length, width, assets: 'warm', initialAdmissionSettled: reads() >= 2 },
        topology: /^[0-9a-f]{40,64}$/.test(process.env.GITHUB_SHA ?? '') ? 'exact release images through Nginx' : 'unverified runtime',
        cachedDetailMs: openingMs, clickMs, readiness: 'visible editable title/source and full preview, single browser observation', budgetMs: 200,
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
