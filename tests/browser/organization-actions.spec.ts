import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { openOrganizationActions } from './organizationActionAdmission';
import { openAdmittedReview, tabAdmittedFocus } from './keyboardAdmission';

const rollback = process.env.STRATAAI_E2E_WORK_UX_ROLLBACK === '1';
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-03-VS-06-T-03/AC-01/02/03: ${rollback ? 'rollback' : 'default'} permitted Organization actions and canonical return at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 900 });
    const participant = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 900 } });
    const headers = { 'X-StrataAI-Request': '1' }, mutationHeaders = () => ({ ...headers, 'Idempotency-Key': crypto.randomUUID() });
    let releaseReads = () => {}, holding = false, gate = Promise.resolve(), heldReads = 0;
    let restoreWorker = () => {};
    try {
      const account = { email: `organization-actions-${crypto.randomUUID()}@example.test`, password: 'organization-actions-correct-horse', displayName: 'Organization action owner' };
      const invited = { ...account, email: `organization-actions-participant-${crypto.randomUUID()}@example.test`, displayName: 'Current action participant' };
      await registerNotificationAccount(context.request, account); await registerNotificationAccount(participant.request, invited);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers: mutationHeaders(), data: { name: 'Organization actions scope' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const createdBoard = await context.request.post('/boards', { headers: mutationHeaders(), data: { organizationId: org, name: 'Permitted action Board', visibility: 'ORGANIZATION' } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
      const createdList = await context.request.post(`/boards/${board}/lists`, { headers: mutationHeaders(), data: { name: 'Permitted action List' } });
      expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
      const createdCard = await context.request.post(`/lists/${list}/cards`, { headers: mutationHeaders(), data: { title: 'Permitted action Card' } });
      expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
      restoreWorker = scopedBoardWorker(org);
      await waitForBoardDelivery(context.request, board);
      const directoryPath = `/app/${org}`, organizationPath = `/organizations/${org}`, boardPath = `${directoryPath}/boards/${board}`;
      async function canonical() {
        const response = await context.request.get(`/boards/${board}`); expect(response.status()).toBe(200);
        const value = await response.json(); expect(value.board).toMatchObject({ id: board });
        return { board: value.board, lists: value.lists };
      }
      const before = await canonical(), graphWrites: string[] = [];
      const observe = (client: Page) => client.on('request', request => {
        const path = new URL(request.url()).pathname;
        if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) && [board, list, card].some(id => path.split('/').includes(id)))
          graphWrites.push(`${request.method()} ${path}`);
      });
      observe(page);
      async function layout(client: Page) {
        expect(await client.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      }
      async function currentActions(client: Page, role: 'Owner' | 'Admin' | 'Member') {
        const create = client.getByRole('button', { name: 'Create board', exact: true }); await expect(create).toBeVisible();
        const summary = client.locator('summary').filter({ hasText: /^Organization actions$/ });
        if (rollback) await expect(summary).toHaveCount(0);
        else {
          await expect(summary).toBeVisible();
          // Admission may withdraw both controls after visibility was observed.
          // Compare contemporaneous admitted nodes, never an obsolete handle.
          await expect.poll(() => create.evaluateAll(elements => {
            const current = elements.length === 1 ? elements[0] : undefined;
            const summary = Array.from(document.querySelectorAll('summary')).find(element => element.textContent?.trim() === 'Organization actions');
            return !!current && !!summary && current.isConnected && summary.isConnected
              && Boolean(current.compareDocumentPosition(summary) & Node.DOCUMENT_POSITION_FOLLOWING);
          })).toBe(true);
        }
        const permitted = client.getByRole('link', { name: role === 'Member' ? 'Leave Organization' : 'Organization settings', exact: true });
        await openOrganizationActions(client, permitted); await expect(permitted).toBeVisible();
        if (role === 'Member') await expect(client.getByRole('link', { name: 'Organization settings', exact: true, includeHidden: true })).toHaveCount(0);
        else await expect(client.getByRole('link', { name: 'Organization settings', exact: true })).toHaveAttribute('href', `${directoryPath}/settings`);
        if (role === 'Owner') await expect(client.getByRole('link', { name: 'Request Organization deletion', exact: true })).toBeVisible();
        else await expect(client.getByRole('link', { name: 'Request Organization deletion', exact: true, includeHidden: true })).toHaveCount(0);
        if (!rollback) {
          // Collapsed actions stay mounted but leave keyboard/accessibility navigation.
          const details = summary.locator('..');
          const permittedName = role === 'Member' ? 'Leave Organization' : 'Organization settings';
          await summary.focus(); await client.keyboard.press('Enter');
          await expect(details).toHaveJSProperty('open', false);
          await expect(client.getByRole('link', { name: permittedName, exact: true, includeHidden: true })).toHaveCount(1);
          await expect(permitted).toHaveCount(0);
          await summary.focus(); await expect(summary).toBeFocused();
          const nextBoard = client.getByRole('link', { name: 'Permitted action Board', exact: true });
          await tabAdmittedFocus(summary, nextBoard, 'Tab');
          await expect(client.getByRole('link', { name: 'Permitted action Board', exact: true })).toBeFocused();
          await tabAdmittedFocus(nextBoard, summary, 'Shift+Tab'); await expect(summary).toBeFocused();
          await openOrganizationActions(client, permitted);
          await expect(details).toHaveJSProperty('open', true); await expect(permitted).toBeVisible();
        }
        const focusTarget = rollback ? permitted : summary;
        await client.keyboard.press('Tab');
        // A foreground admission read can retire the focused control. Establish
        // only current read-only focus, then measure that same connected node.
        await expect(async () => {
          await focusTarget.focus({ timeout: 500 }); await expect(focusTarget).toBeFocused({ timeout: 500 });
          expect(await focusTarget.evaluateAll(elements => {
            const element = elements.length === 1 ? elements[0] : undefined;
            if (!element?.isConnected || element !== document.activeElement) return false;
            const box = element.getBoundingClientRect(), header = document.querySelector('header')?.getBoundingClientRect();
            return box.height >= 44 && box.top >= (header?.bottom ?? 0) && box.bottom <= window.innerHeight
              && element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2));
          })).toBe(true);
        }).toPass({ timeout: 5_000 });
        await layout(client);
        await client.screenshot({ path: test.info().outputPath(`organization-actions-${rollback ? 'rollback' : 'default'}-${role.toLowerCase()}-viewport-${width}.png`), fullPage: false });
      }
      await page.goto(directoryPath);
      if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      await currentActions(page, 'Owner');
      async function issue(role: 'ADMIN' | 'MEMBER') {
        const issued = await context.request.post(`${organizationPath}/invitations`, { headers: mutationHeaders(), data: { email: invited.email, surface: 'INTERNAL', targetRole: role } });
        expect(issued.status()).toBe(201); const id = (await issued.json()).id;
        expect((await participant.request.post(`/me/invitations/${id}/accept`, { headers: mutationHeaders() })).status()).toBe(200);
      }
      await issue('ADMIN');
      const participantPage = await participant.newPage(); observe(participantPage); let documents = 0;
      participantPage.on('request', request => { if (request.isNavigationRequest() && request.frame() === participantPage.mainFrame()) documents++; });
      await participantPage.goto(directoryPath);
      if (width === 768) await participantPage.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      await currentActions(participantPage, 'Admin');
      await openAdmittedReview(participantPage.getByRole('link', { name: 'Organization settings', exact: true }), participantPage.getByLabel(/^Organization name/));
      await expect(participantPage).toHaveURL(new RegExp(`${directoryPath}/settings$`));
      await expect(participantPage.getByLabel(/^Organization name/)).toHaveValue('Organization actions scope');
      await participantPage.goBack(); await expect(participantPage).toHaveURL(new RegExp(directoryPath + '$'));
      await currentActions(participantPage, 'Admin'); expect(documents).toBe(1);
      await participantPage.route(url => url.pathname === organizationPath, async route => {
        if (holding && route.request().method() === 'GET') { heldReads++; await gate; }
        await route.continue();
      });
      gate = new Promise<void>(resolve => { releaseReads = resolve; }); holding = true;
      await issue('MEMBER');
      await expect.poll(() => heldReads, { timeout: 30_000 }).toBeGreaterThan(0);
      await expect(participantPage.getByRole('link', { name: 'Organization settings', exact: true, includeHidden: true })).toHaveCount(0);
      await expect(participantPage.locator('summary').filter({ hasText: /^Organization actions$/ })).toHaveCount(0);
      holding = false; releaseReads();
      await currentActions(participantPage, 'Member'); expect(documents).toBe(1);
      const denied = await participant.request.patch(organizationPath, { headers: mutationHeaders(), data: { name: 'Unauthorized replacement', description: null, logoUrl: null, version: 1 } });
      expect(denied.status()).toBe(404); expect((await denied.json()).code).toBe('organization_not_found');
      const unchanged = await context.request.get(organizationPath); expect(unchanged.status()).toBe(200);
      expect((await unchanged.json()).organization.name).toBe('Organization actions scope');
      await participantPage.goto(`${directoryPath}/settings`);
      await expect(participantPage.getByText('Organization settings are unavailable to your account.', { exact: true })).toBeVisible();
      await expect(participantPage.getByLabel(/^Organization name/)).toHaveCount(0);
      await participantPage.goBack(); await expect(participantPage).toHaveURL(new RegExp(directoryPath + '$'));
      if (width === 768) await participantPage.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      const boardLink = participantPage.getByRole('link', { name: 'Permitted action Board', exact: true });
      const cardLink = participantPage.getByRole('link', { name: 'Permitted action Card', exact: true });
      // Foreground reads can replace the directory link between focus and Enter.
      // Admit only this read-only opening until its real Board target appears.
      await openAdmittedReview(boardLink, cardLink);
      await expect(participantPage).toHaveURL(new RegExp(boardPath + '$'));
      await cardLink.focus(); await cardLink.press('Enter');
      await expect(participantPage.getByRole('dialog', { name: 'Card details', exact: true })).toBeVisible();
      await expect(participantPage.getByRole('textbox', { name: 'Card title', exact: true })).toHaveValue('Permitted action Card');
      await participantPage.goBack(); await expect(participantPage).toHaveURL(new RegExp(boardPath + '$'));
      await expect(cardLink).toBeFocused(); await layout(participantPage);
      expect(await canonical()).toEqual(before); expect(graphWrites).toEqual([]);
      expect((await participant.request.post('/auth/logout', { headers })).status()).toBe(204);
      await participantPage.evaluate(() => window.dispatchEvent(new Event('focus')));
      await expect(participantPage).toHaveURL(/\/login$/);
      await expect(participantPage.getByText('Permitted action Card', { exact: true })).toHaveCount(0);
      await expect(participantPage.getByRole('navigation', { name: 'Internal application navigation', exact: true })).toHaveCount(0);
      expect(graphWrites).toEqual([]);
    } finally {
      holding = false; releaseReads();
      try { restoreWorker(); } finally { await participant.close(); }
    }
  });
}
