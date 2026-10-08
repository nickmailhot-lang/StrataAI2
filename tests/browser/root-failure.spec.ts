import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`ARCH-02/PRD-06: fatal application root has private-safe keyboard recovery at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `root-${width}-${Date.now()}@example.test`, password: 'root-correct-horse-password', displayName: 'Root fixture' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Root recovery fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Root recovery', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Canonical Card after recovery' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const baseline = await context.request.get(`/boards/${board}`); expect(baseline.status()).toBe(200);
    const canonical = (await baseline.json()).lists;
    const sentinel = 'private-root-failure-sentinel';
    await page.addInitScript(privateMaterial => {
      // Force an actual App render failure during router construction. The fault
      // is armed once; a new document after explicit reload has normal history.
      if (sessionStorage.getItem('root-failure-fixture-consumed')) return;
      sessionStorage.setItem('root-failure-fixture-consumed', '1');
      Object.defineProperty(window.history, 'state', { get: () => { throw new Error(privateMaterial); } });
    }, sentinel);
    const diagnostics: string[] = [];
    page.on('console', message => { if (message.type() === 'error') diagnostics.push(message.text()); });
    page.on('pageerror', error => diagnostics.push(error.message));
    let mutations = 0; let documents = 0;
    page.on('request', request => {
      // Router replaceState also emits framenavigated; only a document request
      // proves reload. Do not count an in-document history update as a reload.
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      const path = new URL(request.url()).pathname;
      if (!['GET', 'HEAD'].includes(request.method()) && /^\/(organizations|boards|lists|cards|attachments|labels)(\/|$)/.test(path)
        && !path.endsWith('/live/negotiate')) mutations++;
    });
    const report = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === '/me/activity-client-events'
      && response.request().postDataJSON()?.events?.some((event: { action?: string }) => event.action === 'application_root_exception'));
    await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
    const heading = page.getByRole('heading', { name: 'This application is unavailable.' });
    await expect(heading).toBeFocused();
    await expect(page.getByRole('alert')).toContainText('A submitted change may still have completed.');
    await expect(page.getByText('Reloading may discard unsaved changes.')).toBeVisible();
    const received = await report; expect(received.status()).toBe(204);
    const batch = received.request().postDataJSON();
    expect(batch.events).toEqual([{ action: 'application_root_exception', kind: 'exception', count: 1 }]);
    for (const secret of [sentinel, org, board, card, account.email]) expect(JSON.stringify(batch)).not.toContain(secret);
    expect(diagnostics).toEqual(['A view could not be rendered.']);
    expect(await page.locator('body').innerText()).not.toContain(sentinel);
    expect(documents).toBe(1); expect(mutations).toBe(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.keyboard.press('Tab');
    const reload = page.getByRole('button', { name: 'Reload this page' }); await expect(reload).toBeFocused();
    expect(await reload.evaluate(node => getComputedStyle(node).outlineStyle)).toBe('solid');
    expect(await reload.evaluate(node => node.getBoundingClientRect().height)).toBeGreaterThanOrEqual(44);
    await Promise.all([page.waitForEvent('load'), page.keyboard.press('Enter')]);
    await expect(page.getByRole('textbox', { name: 'Card title' })).toHaveValue('Canonical Card after recovery');
    await expect(heading).toHaveCount(0);
    expect(documents).toBe(2); expect(mutations).toBe(0);
    const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
    expect((await current.json()).lists).toEqual(canonical);
  });
}
