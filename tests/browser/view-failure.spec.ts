import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const kind of ['board', 'card'] as const) {
  test(`PRD-06: private-safe ${kind} render failure and explicit recovery at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `render-${kind}-${width}-${Date.now()}@example.test`, password: 'render-correct-horse-password', displayName: 'Render fixture' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Render recovery' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Render recovery', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Unchanged canonical Card' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const baselineReply = await context.request.get(`/boards/${board}`); expect(baselineReply.status()).toBe(200);
    const baseline = await baselineReply.json();
    const privateMaterial = 'private-render-diagnostic-sentinel';
    let failing = true;
    await page.route(url => url.pathname === `/boards/${board}`, async route => {
      const response = await route.fetch();
      if (!failing) { await route.fulfill({ response }); return; }
      const body = await response.json();
      // Controlled bad read forces an actual React render failure; server state remains canonical.
      body.lists[0].cards[0].title = { [privateMaterial]: org };
      await route.fulfill({ response, json: body });
    });
    const diagnostics: string[] = [];
    page.on('console', message => { if (message.type() === 'error') diagnostics.push(message.text()); });
    let mutations = 0;
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      // Navigation observations and hub negotiation do not mutate canonical work.
      if (!['GET', 'HEAD'].includes(request.method()) && /^\/(organizations|boards|lists|cards|attachments|labels)(\/|$)/.test(path)
        && !path.endsWith('/live/negotiate')) mutations++;
    });
    const report = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === '/me/activity-client-events'
      && response.request().postDataJSON()?.events?.some((event: { action?: string }) => event.action === `${kind}_render`));
    await page.goto(`/app/${org}/boards/${board}${kind === 'card' ? `/cards/${card}` : ''}`);
    await expect(page.getByRole('alert')).toContainText('This view is unavailable.');
    await expect(page.getByText('Reloading may discard unsaved changes.')).toBeVisible();
    expect(await page.locator('body').innerText()).not.toContain(privateMaterial);
    const received = await report; expect(received.status()).toBe(204);
    const batch = received.request().postDataJSON();
    expect(batch.events).toContainEqual({ action: `${kind}_render`, kind: 'exception', count: 1 });
    const serialized = JSON.stringify(batch);
    for (const secret of [privateMaterial, org, board, card, account.email]) expect(serialized).not.toContain(secret);
    expect(diagnostics.join('\n')).not.toContain(privateMaterial);
    expect(mutations).toBe(0);
    failing = false;
    const reload = page.getByRole('button', { name: 'Reload this page' }); await reload.focus();
    await Promise.all([page.waitForEvent('load'), reload.press('Enter')]);
    if (kind === 'card') await expect(page.getByRole('textbox', { name: 'Card title' })).toBeEnabled();
    else await expect(page.getByRole('link', { name: 'Unchanged canonical Card', exact: true })).toBeVisible();
    expect(mutations).toBe(0);
    const currentReply = await context.request.get(`/boards/${board}`); expect(currentReply.status()).toBe(200);
    expect((await currentReply.json()).lists).toEqual(baseline.lists);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
