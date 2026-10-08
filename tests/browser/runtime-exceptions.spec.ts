import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-06: browser exceptions retain a dirty Card draft and report only fixed counts at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `runtime-${width}-${Date.now()}@example.test`, password: 'runtime-correct-horse-password', displayName: 'Runtime fixture' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Runtime exception fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Runtime fixture', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Unchanged canonical Card' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const baseline = await context.request.get(`/boards/${board}`); expect(baseline.status()).toBe(200);
    const canonical = (await baseline.json()).lists;
    const sentinel = 'private-runtime-exception-sentinel';
    const diagnostics: string[] = [];
    page.on('console', message => { if (message.type() === 'error') diagnostics.push(message.text()); });
    page.on('pageerror', error => diagnostics.push(error.message));
    let mutations = 0;
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (!['GET', 'HEAD'].includes(request.method()) && /^\/(organizations|boards|lists|cards|attachments|labels)(\/|$)/.test(path)
        && !path.endsWith('/live/negotiate')) mutations++;
    });
    await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
    const title = page.getByRole('textbox', { name: 'Card title' }); await expect(title).toBeEnabled();
    await title.fill('Unsaved private draft');
    const report = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === '/me/activity-client-events'
      && response.request().postDataJSON()?.events?.some((event: { action?: string }) => event.action === 'application_promise_exception'));
    // Actual browser listener errors and unhandled rejection, not synthetic ErrorEvents.
    await page.evaluate(privateMaterial => {
      const button = document.createElement('button'); button.textContent = 'Runtime fixture trigger';
      button.addEventListener('click', () => { throw new Error(privateMaterial); });
      document.body.append(button); button.click(); button.remove();
      void Promise.reject(new Error(privateMaterial));
      void Promise.reject(new Error('handled-private-failure')).catch(() => {});
    }, sentinel);
    const received = await report; expect(received.status()).toBe(204);
    const batch = received.request().postDataJSON();
    expect(batch.events).toContainEqual({ action: 'application_event_exception', kind: 'exception', count: 1 });
    expect(batch.events).toContainEqual({ action: 'application_promise_exception', kind: 'exception', count: 1 });
    for (const secret of [sentinel, org, board, card, account.email, 'Unsaved private draft'])
      expect(JSON.stringify(batch)).not.toContain(secret);
    expect(diagnostics.join('\n')).not.toContain(sentinel);
    await expect(title).toHaveValue('Unsaved private draft');
    expect(mutations).toBe(0);
    const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
    expect((await current.json()).lists).toEqual(canonical);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
