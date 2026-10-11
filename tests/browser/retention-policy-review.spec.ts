import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

const headers = { 'X-StrataAI-Request': '1' };
const rule = { recordClass: 'governance', category: `minutes_${'m'.repeat(56)}`, jurisdiction: `Reviewed applicability ${'J'.repeat(70)}`,
  anchor: 'approved', retentionDays: null, action: 'preserve', source: 'Original private reviewed source', notes: 'An explicit fixture rule, not a statutory default.' };
async function keyboardClick(page: Page, control: ReturnType<Page['getByRole']>) {
  await expect(control).toBeEnabled(); await control.focus(); await page.keyboard.press('Enter');
}
async function choose(page: Page, name: string, option: string) {
  const select = page.getByRole('combobox', { name, exact: true }); await select.focus(); await page.keyboard.press('ArrowDown');
  await page.getByRole('option', { name: option, exact: true }).focus(); await page.keyboard.press('Enter');
}
async function approve(page: Page) {
  await keyboardClick(page, page.getByRole('button', { name: 'Review policy change', exact: true }));
  const dialog = page.getByRole('dialog', { name: 'Review policy change', exact: true }); await expect(dialog).toBeVisible();
  const confirm = dialog.getByRole('button', { name: 'Confirm policy change', exact: true }); await expect(confirm).toBeDisabled();
  await dialog.getByRole('checkbox', { name: 'I reviewed the source and applicability.', exact: true }).focus(); await page.keyboard.press('Space');
  await dialog.getByRole('button', { name: 'Close and retain work', exact: true }).focus(); await page.keyboard.press('Tab');
  expect(await dialog.evaluate(node => node.contains(document.activeElement))).toBe(true);
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  await keyboardClick(page, confirm);
}
async function noHorizontalOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}
function metadataAdmission(page: Page) {
  let received = 0;
  page.on('websocket', socket => {
    if (!new URL(socket.url()).pathname.endsWith('/organizations/live/metadata')) return;
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string') return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try { if (JSON.parse(part).type === 2) received++; } catch { /* Partial handshake; no admitted source. */ }
      }
    });
  });
  return () => received;
}

for (const width of [1280, 390]) {
  test(`PRD-70: real policy creation, stale review and retained history with keyboard confirmation at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const metadata = metadataAdmission(page);
    const owner = await registerNotificationAccount(context.request, { email: `retention-review-${width}-${crypto.randomUUID()}@example.test`,
      password: 'retention-fixture-correct-horse', displayName: 'Historical retention Owner' }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Retention policy keyboard fixture' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
    const restoreWorker = scopedBoardWorker(organization);
    try {
    const path = `/organizations/${organization}/retention-policies`;
    await page.goto(`/app/${organization}/retention-policies`);
    await expect.poll(metadata).toBeGreaterThan(0);
    await expect(page.getByText(/No retention policies are recorded in this page/)).toBeVisible();
    await keyboardClick(page, page.getByRole('button', { name: 'Create policy', exact: true }));
    await expect(page.getByRole('dialog', { name: 'Create retention policy', exact: true })).toBeVisible();
    await choose(page, 'Record class', 'Governance'); await choose(page, 'Period starts at', 'approval'); await choose(page, 'Disposition action', 'Preserve');
    await page.getByLabel('Category', { exact: true }).fill(rule.category);
    await page.getByLabel('Jurisdiction (blank = Organization-wide)', { exact: true }).fill(rule.jurisdiction);
    await page.getByLabel('Reviewed source', { exact: true }).fill(rule.source); await page.getByLabel('Notes (optional)', { exact: true }).fill(rule.notes);
    await approve(page); await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Retention policies', exact: true })).toBeFocused();
    await noHorizontalOverflow(page);
    const firstPage = await context.request.get(path); expect(firstPage.status()).toBe(200);
    const first = (await firstPage.json()).items; expect(first).toHaveLength(1); expect(first[0]).toMatchObject({ ...rule, version: 1 });
    const policy = first[0].id;
    await keyboardClick(page, page.getByRole('button', { name: `Edit policy for Governance ${rule.category}, ${rule.jurisdiction}`, exact: true }));
    await page.getByLabel('Reviewed source', { exact: true }).fill('Preserved private stale draft');
    const concurrent = await context.request.patch(`${path}/${policy}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { version: 1, policy: { ...rule, retentionDays: 45, source: 'Concurrent private reviewed source' } } });
    expect(concurrent.status()).toBe(200); expect((await concurrent.json()).version).toBe(2);
    await approve(page); await expect(page.getByRole('alert').filter({ hasText: 'This submission was refused.' })).toBeVisible();
    await keyboardClick(page, page.getByRole('button', { name: 'Review current policy', exact: true }));
    await expect(page.getByLabel('Reviewed source', { exact: true })).toHaveValue('Preserved private stale draft');
    await expect(page.getByText('Source: Concurrent private reviewed source', { exact: true })).toBeVisible();
    await approve(page); await expect(page.getByRole('dialog')).toHaveCount(0);
    const current = await context.request.get(`${path}/${policy}`); expect(current.status()).toBe(200);
    expect(await current.json()).toMatchObject({ ...rule, version: 3, source: 'Preserved private stale draft' });
    await keyboardClick(page, page.getByRole('button', { name: `View history for Governance ${rule.category}, ${rule.jurisdiction}`, exact: true }));
    await expect(page.getByRole('heading', { name: 'Retained policy history', exact: true })).toBeFocused();
    const history = page.getByRole('region', { name: 'Retained policy history', exact: true });
    await expect(history.getByText(`Historical retention Owner · Organization owner`, { exact: false })).toHaveCount(3);
    const retained = await context.request.get(`${path}/${policy}/history`); expect(retained.status()).toBe(200);
    const revisions = (await retained.json()).items;
    expect(revisions.map((row: { policy: { version: number } }) => row.policy.version)).toEqual([3, 2, 1]);
    expect(revisions.every((row: { actorId: string; actorLabel: string; actingCapacity: number }) => row.actorId === owner.user.id
      && row.actorLabel === 'Historical retention Owner' && row.actingCapacity === 0)).toBe(true);
    expect(new Set(revisions.map((row: { eventId: string }) => row.eventId)).size).toBe(3);
    expect(revisions[2].policy).toMatchObject(rule); await noHorizontalOverflow(page);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { restoreWorker(); }
  });

  test(`PRD-70: original retry after reload and administrator withdrawal protect private policy work at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const metadata = metadataAdmission(page);
    const production = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {};
    try {
      await registerNotificationAccount(owner.request, { email: `retention-owner-${width}-${crypto.randomUUID()}@example.test`,
        password: 'retention-owner-correct-horse', displayName: 'Retention fixture Owner' }, production);
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Retention original retry fixture' } });
      expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
      restoreWorker = scopedBoardWorker(organization);
      const email = `retention-admin-${width}-${crypto.randomUUID()}@example.test`;
      const admin = await registerNotificationAccount(context.request, { email, password: 'retention-admin-correct-horse', displayName: 'Retention fixture administrator' }, production);
      const invitation = await owner.request.post(`/organizations/${organization}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(invitation.status()).toBe(201); const invited = await invitation.json();
      expect((await context.request.post(`/me/invitations/${invited.id}/accept`, { headers })).status()).toBe(200);
      const policy = crypto.randomUUID(), path = `/organizations/${organization}/retention-policies/${policy}`;
      expect((await owner.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { version: 0, policy: rule } })).status()).toBe(200);
      const writes: { key: string; body: string }[] = [];
      await page.route(url => url.pathname === path, async route => {
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (writes.length === 1 || writes.length === 3) await route.abort('timedout'); else await route.fulfill({ response });
      });
      const editName = `Edit policy for Governance ${rule.category}, ${rule.jurisdiction}`;
      await page.goto(`/app/${organization}/retention-policies`); await expect.poll(metadata).toBeGreaterThan(0);
      await keyboardClick(page, page.getByRole('button', { name: editName, exact: true }));
      await page.getByLabel('Reviewed source', { exact: true }).fill('Original administrator private proposal'); await approve(page);
      await expect(page.getByText(/The acknowledgment could not be confirmed/)).toBeVisible();
      expect(writes).toHaveLength(1);
      expect((await owner.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 2, policy: { ...rule, source: 'Later authoritative private source' } } })).status()).toBe(200);
      const beforeReload = metadata(); await page.reload();
      await expect.poll(metadata).toBeGreaterThan(beforeReload);
      const recovery = page.getByRole('dialog', { name: 'Recover original policy submission', exact: true }); await expect(recovery).toBeVisible();
      await expect(recovery.getByText('Source: Original administrator private proposal', { exact: true })).toBeVisible();
      await keyboardClick(page, recovery.getByRole('button', { name: 'Retry original submission', exact: true }));
      await expect.poll(() => writes.length).toBe(2);
      await expect(page.getByRole('status').filter({ hasText: 'The original policy change was acknowledged. Refreshing current policies.' })).toBeVisible();
      await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByText('Source: Later authoritative private source', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      const originalHistory = await owner.request.get(`${path}/history`); expect(originalHistory.status()).toBe(200);
      const originalRevision = (await originalHistory.json()).items.find((row: { policy: { version: number } }) => row.policy.version === 2);
      expect(originalRevision).toMatchObject({ actorId: admin.user.id, actorLabel: 'Retention fixture administrator', actingCapacity: 1,
        policy: { source: 'Original administrator private proposal' } });
      await keyboardClick(page, page.getByRole('button', { name: editName, exact: true }));
      await page.getByLabel('Reviewed source', { exact: true }).fill('Pending private proposal before withdrawal'); await approve(page);
      await expect(page.getByText(/The acknowledgment could not be confirmed/)).toBeVisible(); expect(writes).toHaveLength(3);
      await keyboardClick(page, page.getByRole('button', { name: 'Close and retain work', exact: true }));
      const member = await owner.request.get(`/organizations/${organization}/members/${admin.user.id}`); expect(member.status()).toBe(200);
      const membership = (await member.json()).member;
      expect((await owner.request.delete(`/organizations/${organization}/members/${admin.user.id}?expectedVersion=${membership.version}`,
        { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } })).status()).toBe(204);
      await keyboardClick(page, page.getByRole('button', { name: 'Refresh policies', exact: true }));
      await expect(page.getByText('Retention policies are no longer available with your current access.', { exact: true })).toBeVisible();
      await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByRole('button', { name: 'Recover original policy submission', exact: true })).toHaveCount(0);
      const visible = await page.locator('body').innerText();
      for (const value of [rule.source, 'Original administrator private proposal', 'Later authoritative private source', 'Pending private proposal before withdrawal']) expect(visible).not.toContain(value);
      expect(await page.evaluate(({ actor, org }) => sessionStorage.getItem(`strataai:retention-policy-change:v1:${actor}:${org}`) === null,
        { actor: admin.user.id, org: organization })).toBe(true);
      for (const endpoint of [path, `${path}/history`]) {
        const denied = await context.request.get(endpoint); expect(denied.status()).toBe(404); expect(await denied.text()).not.toContain('private');
      }
      const replay = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': writes[2].key }, data: JSON.parse(writes[2].body) });
      expect(replay.status()).toBe(404); expect(await replay.text()).not.toContain('Pending private proposal');
      const retained = await owner.request.get(path); expect(retained.status()).toBe(200);
      expect(await retained.json()).toMatchObject({ version: 4, source: 'Pending private proposal before withdrawal' });
      await noHorizontalOverflow(page);
    } finally { restoreWorker(); await owner.close(); }
  });
}
