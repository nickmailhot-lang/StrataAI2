import { randomUUID } from 'node:crypto';
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';

type ProviderEntry = { key: string; id: string; attempts: number; payload: { to: string[]; subject: string; text: string } };
type Outage = { active: boolean; attempts: { key: string; attempts: number }[] };
test.describe.configure({ timeout: 360_000 });

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`ARCH-08/PRD-02/07/08: keyboard task work survives mail outage at ${viewport.width}px`, async ({ page, context, browser }) => {
    expect(process.env.STRATAAI_E2E_IDENTITY_MAIL).toBe('1');
    const revision = process.env.STRATAAI_IMAGE_TAG ?? '';
    const instance = process.env.STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE ?? '';
    expect(revision).toMatch(/^[a-f0-9]{40}$|^[a-f0-9]{64}$/); expect(instance).not.toBe('');
    const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
    const runtimeBody = await runtime.json();
    expect({ service: runtimeBody.service, mode: runtimeBody.mode, revision: runtimeBody.revision })
      .toEqual({ service: 'strataai-api', mode: 'production', revision });
    const provider = process.env.STRATAAI_E2E_IDENTITY_PROVIDER_URL ?? 'http://localhost:19090';
    // Read the fixture mailbox outside browser tracing; verification still uses the real API.
    async function peer<T>(path: string, data?: object): Promise<T> {
      const response = await fetch(provider + path, { signal: AbortSignal.timeout(5_000),
        ...(data ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) } : {}) });
      expect(response.status).toBe(200); return await response.json() as T;
    }
    async function ownedProvider() {
      expect(await peer('/health')).toEqual({ ready: true, instance });
    }
    await ownedProvider();
    expect((await peer<Outage>('/outage')).active).toBe(false);
    const email = `core-mail-browser-${randomUUID()}@example.test`;
    const credentials = { email, password: 'core-mail-browser-correct-horse', displayName: 'Mail outage owner' };
    const headers = { 'X-StrataAI-Request': '1' };
    const registered = await context.request.post('/auth/register', { headers, data: credentials });
    expect(registered.status()).toBe(201); expect((await registered.json()).user.emailVerified).toBe(false);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(403);
    let verification = '';
    await expect.poll(async () => {
      const entry = (await peer<ProviderEntry[]>('/messages')).find(item => item.payload.to.includes(email)
        && item.payload.subject === 'Verify your StrataAI2 email' && item.attempts >= 2);
      verification = entry?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
      return Boolean(verification);
    }, { timeout: 45_000, intervals: [500, 1000] }).toBe(true);
    expect((await context.request.post('/auth/verify-email', { headers: { ...headers, 'Idempotency-Key': randomUUID() },
      data: { token: verification } })).status()).toBe(200);
    verification = '';
    await page.setViewportSize(viewport); await page.goto('/login');
    await page.getByLabel(/^Email/).fill(email); await page.getByLabel(/^Password/).fill(credentials.password);
    await page.getByLabel(/^Password/).press('Enter'); await expect(page).toHaveURL(/\/app$/);
    const organization = await context.request.post('/organizations', { headers: { ...headers, 'Idempotency-Key': randomUUID() },
      data: { name: 'Mail outage organization' } });
    expect(organization.status()).toBe(201); const organizationId = (await organization.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': randomUUID() },
      data: { organizationId, name: 'Mail outage Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const boardId = (await createdBoard.json()).id;
    const boardPath = `/app/${organizationId}/boards/${boardId}`;
    await page.goto(boardPath); await expect(page.getByRole('heading', { name: 'Mail outage Board', exact: true })).toBeVisible();
    async function accessible(view: Page = page) {
      expect((await new AxeBuilder({ page: view }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await view.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
    await accessible();
    const previous = new Set((await peer<Outage>('/outage')).attempts.map(item => item.key));
    let enabled = false;
    try {
      await ownedProvider(); enabled = true;
      expect(await peer('/control', { outage: true })).toEqual({ outage: true });
      const recovery = await context.newPage(); await recovery.setViewportSize(viewport);
      await recovery.goto('/forgot-password'); await recovery.getByLabel(/^Email/).fill(email);
      await recovery.getByLabel(/^Email/).press('Enter');
      await expect(recovery.getByRole('status')).toContainText('Request received.'); await accessible(recovery);
      let outageKey = '';
      await expect.poll(async () => {
        const observed = await peer<Outage>('/outage'); expect(observed.active).toBe(true);
        const fresh = observed.attempts.filter(item => !previous.has(item.key));
        if (fresh.length !== 1 || fresh[0].attempts < 1) return false;
        outageKey = fresh[0].key; return true;
      }, { timeout: 45_000, intervals: [500, 1000] }).toBe(true);
      expect(outageKey).toMatch(/^strataai-identity\/reset\/[a-f0-9]{32}$/);
      expect((await peer<ProviderEntry[]>('/messages')).filter(item => item.key === outageKey)).toHaveLength(0);
      expect((await context.request.get('/readyz')).status()).toBe(200);
      await page.bringToFront();
      const counts = { lists: 0, cards: 0, edits: 0 };
      let cardId = '';
      page.on('request', request => {
        const path = new URL(request.url()).pathname;
        if (request.method() === 'POST' && path === `/boards/${boardId}/lists`) counts.lists++;
        if (request.method() === 'POST' && /^\/lists\/[a-f0-9-]+\/cards$/.test(path)) counts.cards++;
        if (request.method() === 'PATCH' && path === `/cards/${cardId}`) counts.edits++;
      });
      const listDialog = page.getByRole('dialog', { name: 'Add list', exact: true });
      await openAdmittedReview(page.getByRole('button', { name: 'Add list', exact: true }), listDialog);
      await listDialog.getByLabel('List name', { exact: false }).fill('Outage work');
      const [listReply] = await Promise.all([page.waitForResponse(response => response.request().method() === 'POST'
        && new URL(response.url()).pathname === `/boards/${boardId}/lists`),
      pressObservedRequest(listDialog.getByRole('button', { name: 'Create', exact: true }), () => counts.lists, 1)]);
      expect(listReply.status()).toBe(201); const listId = (await listReply.json()).id;
      await expect(page.getByRole('heading', { name: 'Outage work', exact: true })).toBeVisible();
      const cardDialog = page.getByRole('dialog', { name: 'Add card', exact: true });
      await openAdmittedReview(page.getByRole('button', { name: 'Add card to Outage work', exact: true }), cardDialog);
      await cardDialog.getByLabel('Card title', { exact: false }).fill('Work during mail outage');
      const [cardReply] = await Promise.all([page.waitForResponse(response => response.request().method() === 'POST'
        && new URL(response.url()).pathname === `/lists/${listId}/cards`),
      pressObservedRequest(cardDialog.getByRole('button', { name: 'Create', exact: true }), () => counts.cards, 1)]);
      expect(cardReply.status()).toBe(201); cardId = (await cardReply.json()).id;
      const cardLink = page.getByRole('link', { name: 'Work during mail outage', exact: true });
      await expect(cardLink).toBeVisible(); await cardLink.focus(); await cardLink.press('Enter');
      const title = page.getByLabel('Card title', { exact: false }); await expect(title).toHaveValue('Work during mail outage');
      await title.fill('Persisted through mail outage');
      const [editReply] = await Promise.all([page.waitForResponse(response => response.request().method() === 'PATCH'
        && new URL(response.url()).pathname === `/cards/${cardId}`),
      pressObservedRequest(page.getByRole('button', { name: 'Save card', exact: true }), () => counts.edits, 1)]);
      expect(editReply.status()).toBe(200);
      const edited = await editReply.json();
      expect({ id: edited.id, version: edited.version }).toEqual({ id: cardId, version: 2 });
      await expect(page.getByRole('status').filter({ hasText: 'Changes saved.' })).toHaveText('Changes saved.');
      await page.reload(); await expect(page.getByLabel('Card title', { exact: false })).toHaveValue('Persisted through mail outage');
      await accessible();
      const snapshot = await context.request.get(`/boards/${boardId}`); expect(snapshot.status()).toBe(200);
      const stored = (await snapshot.json()).lists.flatMap((column: { cards: { id: string; title: string; version: number }[] }) => column.cards)
        .filter((card: { id: string }) => card.id === cardId);
      expect(stored).toHaveLength(1); expect({ title: stored[0].title, version: stored[0].version })
        .toEqual({ title: 'Persisted through mail outage', version: 2 });
      const anonymous = await browser.newContext();
      try {
        const denied = await anonymous.request.get(`${new URL(page.url()).origin}/boards/${boardId}`);
        expect(denied.status()).toBe(404); expect(await denied.text()).not.toContain(cardId);
        expect(await denied.text()).not.toContain('Persisted through mail outage');
      } finally { await anonymous.close(); }
      expect(counts).toEqual({ lists: 1, cards: 1, edits: 1 });
      expect((await peer<Outage>('/outage')).active).toBe(true);
      await ownedProvider(); expect(await peer('/control', { outage: false })).toEqual({ outage: false }); enabled = false;
      await expect.poll(async () => {
        const entries = (await peer<ProviderEntry[]>('/messages')).filter(item => item.key === outageKey);
        if (entries.length !== 1 || !entries[0].payload.to.includes(email)) return false;
        return entries[0].attempts >= 2;
      }, { timeout: 210_000, intervals: [1000, 2000] }).toBe(true);
      expect((await context.request.get('/readyz')).status()).toBe(200);
      await page.reload(); await expect(page.getByLabel('Card title', { exact: false })).toHaveValue('Persisted through mail outage');
      expect((await context.request.get('/me')).status()).toBe(200);
      expect(counts).toEqual({ lists: 1, cards: 1, edits: 1 });
      await recovery.close();
    } finally {
      if (enabled) { await ownedProvider(); expect(await peer('/control', { outage: false })).toEqual({ outage: false }); }
    }
  });
}
