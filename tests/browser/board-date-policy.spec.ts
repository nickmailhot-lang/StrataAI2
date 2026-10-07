import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-12: keyboard timezone policy recovery, live display and accessibility at ${width}px`, async ({ page, context, browser, baseURL }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `date-policy-${width}-${Date.now()}@example.test`, password: 'policy-browser-correct-horse',
      displayName: 'Policy user', timezone: 'UTC', locale: 'en-US' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Policy Organization' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Policy Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Policy List' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Policy Card' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    expect((await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { dueAt: '2040-01-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: false, version: 1 } })).status()).toBe(200);
    const restoreWorker = scopedBoardWorker(org);
    let preferences: Awaited<ReturnType<typeof browser.newContext>> | undefined;
    try {
      preferences = await browser.newContext({ baseURL });
      expect((await preferences.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      await waitForBoardDelivery(context.request, board);
      const boardPath = `/app/${org}/boards/${board}`, policyPath = `${boardPath}/date-policy`;
      const canvasPeer = await context.newPage(); await canvasPeer.setViewportSize({ width, height: 844 });
      // Fix only this viewer's wall clock; server dates and release Worker timers
      // stay real. This instant crosses calendar days between UTC and Honolulu.
      await canvasPeer.clock.setFixedTime(new Date('2040-01-02T23:00:00Z'));
      await canvasPeer.goto(boardPath);
      const canvasCard = canvasPeer.getByRole('link', { name: 'Policy Card', exact: true });
      await expect(canvasCard).toHaveAccessibleDescription('Due soon');
      await expect(canvasPeer.getByText('Live updates connected.', { exact: true })).toBeVisible();
      expect((await new AxeBuilder({ page: canvasPeer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const datesPeer = await context.newPage(); await datesPeer.setViewportSize({ width, height: 844 });
      await datesPeer.goto(`${boardPath}/cards/${card}`);
      const dates = datesPeer.getByRole('region', { name: 'Card dates', exact: true });
      await expect(dates).toContainText('Due Jan 3, 2040');
      await expect(datesPeer.getByText('Live updates connected.', { exact: true })).toBeVisible();
      await page.goto(boardPath);
      const policyLink = page.getByRole('link', { name: 'Board timezone', exact: true }); await expect(policyLink).toBeVisible();
      await policyLink.press('Enter'); await expect(page).toHaveURL(new RegExp('/date-policy$'));
      const input = page.getByRole('textbox', { name: 'Board timezone override', exact: true }); await expect(input).toBeEnabled();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/boards/${board}/date-policy`, async route => {
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response });
      });
      await input.pressSequentially('Pacific/Honolulu');
      await page.getByRole('button', { name: 'Save timezone policy' }).press('Enter');
      const retry = page.getByRole('button', { name: 'Retry timezone change' }); await expect(retry).toBeEnabled();
      await expect(input).toHaveCount(0); await expect(page.getByRole('link', { name: 'Back to Board' })).toHaveAttribute('aria-disabled', 'true');
      await expect(dates).toContainText('Due Jan 2, 2040'); await expect(dates).toContainText('Board timezone policy.');
      await expect(canvasCard).toHaveAccessibleDescription('Due today');
      await retry.press('Enter'); await expect(input).toHaveValue('Pacific/Honolulu');
      await expect(page.getByRole('button', { name: 'Save timezone policy' })).toBeFocused();
      const focusRing = await page.getByRole('button', { name: 'Save timezone policy' }).evaluate(element => {
        const style = getComputedStyle(element); return { style: style.outlineStyle, width: parseFloat(style.outlineWidth) };
      });
      expect(focusRing.style).toBe('solid'); expect(focusRing.width).toBeGreaterThanOrEqual(2);
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ timezone: 'Pacific/Honolulu', version: 1 });
      expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      await page.unroute(`**/boards/${board}/date-policy`);
      const policyPeer = await context.newPage(); await policyPeer.setViewportSize({ width, height: 844 }); await policyPeer.goto(policyPath);
      const peerInput = policyPeer.getByRole('textbox', { name: 'Board timezone override' }); await expect(peerInput).toHaveValue('Pacific/Honolulu');
      await peerInput.press('ControlOrMeta+A'); await peerInput.pressSequentially('UTC');
      await policyPeer.getByRole('button', { name: 'Save timezone policy' }).press('Enter');
      await expect(input).toHaveValue('UTC'); await expect(dates).toContainText('Due Jan 3, 2040');
      await expect(canvasCard).toHaveAccessibleDescription('Due soon');
      await input.press('ControlOrMeta+A'); await input.press('Backspace');
      await page.getByRole('button', { name: 'Save timezone policy' }).press('Enter');
      await expect(input).toHaveValue(''); await expect(peerInput).toHaveValue('');
      await expect(dates).not.toContainText('Board timezone policy.'); await expect(dates).toContainText('Viewing timezone: UTC.');
      const current = (await (await context.request.get(`/boards/${board}`)).json());
      expect(current.board).toMatchObject({ dateTimezoneOverride: null, version: 4 });
      expect(current.lists[0].cards[0]).toMatchObject({ id: card, version: 2 });
      expect(Date.parse(current.lists[0].cards[0].dueAt)).toBe(Date.parse('2040-01-03T08:00:00Z'));
      expect((await (await context.request.get('/me')).json()).timezone).toBe('UTC');
      // Once Board policy is cleared, a different signed-in session's viewing
      // preference reaches both the open Card and canvas without manual reads.
      const currentProfile = await preferences.request.get('/me'); expect(currentProfile.status()).toBe(200);
      expect((await preferences.request.patch('/me', { headers, data: { version: (await currentProfile.json()).version, timezone: 'Pacific/Honolulu' } })).status()).toBe(200);
      await expect(dates).toContainText('Viewing timezone: Pacific/Honolulu.', { timeout: 25_000 });
      await expect(dates).toContainText('Due Jan 2, 2040'); await expect(dates).not.toContainText('Board timezone policy.');
      await expect(canvasCard).toHaveAccessibleDescription('Due today', { timeout: 25_000 });
      const recovered = await context.request.get(`/boards/${board}`); expect(recovered.status()).toBe(200);
      expect(await recovered.json()).toEqual(current);
      expect((await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 2, dueAt: '2040-01-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: true } })).status()).toBe(200);
      await expect(canvasCard).toHaveAccessibleDescription('Complete');
      expect((await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 3, dueHasTime: false, dueComplete: false } })).status()).toBe(200);
      await expect(canvasCard).not.toHaveAttribute('aria-describedby');
      await expect(canvasCard).not.toContainText('Complete');
      expect((await new AxeBuilder({ page: canvasPeer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await canvasPeer.close(); await policyPeer.close(); await datesPeer.close();
    } finally { restoreWorker(); await preferences?.close(); }
  });
}
