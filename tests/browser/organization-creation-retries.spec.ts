import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03-AC-WS-03-01/TC-06/07/11/12: original creation retry preserves later metadata at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `creation-retry-${viewport.width}-${Date.now()}@example.test`,
      password: 'browser-creation-retry-correct-horse', displayName: 'Creation owner' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const account = await context.request.get('/me'); expect(account.status()).toBe(200); const actor = (await account.json()).id;
    await page.goto('/app');
    const opener = page.getByRole('button', { name: 'Create organization', exact: true });
    await opener.focus(); await opener.press('Enter');
    await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toBeFocused();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.getByRole('button', { name: 'Cancel', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog')).toHaveCount(0); await expect(opener).toBeFocused();
    const empty = await context.request.get('/organizations/directory'); expect(empty.status()).toBe(200); expect((await empty.json()).items).toEqual([]);
    const writes: { url: string; key: string | undefined; body: string | null }[] = []; let org: string | undefined;
    await page.route(url => url.pathname === '/organizations', async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      expect(new URL(writes.at(-1)!.url).searchParams.get('expectedActorId')).toBe(actor);
      expect(writes.at(-1)!.key).toMatch(/^[0-9a-f-]{36}$/);
      const response = await route.fetch(); expect(response.status()).toBe(201);
      const original = await response.json(); org = original.organization.id;
      expect(original.organization.ownerUserId).toBe(actor); expect(original.role).toBe(0); expect(original.organization.version).toBe(1);
      if (writes.length === 1) {
        const later = await context.request.patch(`/organizations/${org}`, { headers, data: { name: 'Later creation metadata', version: 1 } });
        expect(later.status()).toBe(200); expect((await later.json()).version).toBe(2);
        await route.abort('timedout');
      } else await route.fulfill({ response });
    });
    await opener.focus(); await opener.press('Enter');
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Original creation metadata');
    await page.getByRole('textbox', { name: 'Description', exact: true }).fill('Reviewed creation');
    await page.getByRole('button', { name: 'Create', exact: true }).focus(); await page.keyboard.press('Enter');
    const retry = page.getByRole('button', { name: 'Retry original creation', exact: true });
    await expect(retry).toBeFocused(); await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toBeDisabled();
    await expect(page.getByRole('textbox', { name: 'Description', exact: true })).toBeDisabled();
    expect(writes).toHaveLength(1); expect(org).toBeTruthy();
    const before = await context.request.get(`/organizations/${org}`); expect(before.status()).toBe(200); const beforeState = await before.json();
    await retry.press('Enter'); await expect(page).toHaveURL(new RegExp(`/app/${org}$`));
    await expect(page.getByRole('heading', { name: 'Later creation metadata', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Original creation metadata', exact: true })).toHaveCount(0);
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
    const after = await context.request.get(`/organizations/${org}`); expect(after.status()).toBe(200); expect(await after.json()).toEqual(beforeState);
    const directory = await context.request.get('/organizations/directory'); expect(directory.status()).toBe(200);
    expect((await directory.json()).items.map((item: { organization: { id: string } }) => item.organization.id)).toEqual([org]);
    const members = await context.request.get(`/organizations/${org}/members`); expect(members.status()).toBe(200);
    const owner = (await members.json()).items; expect(owner).toHaveLength(1); expect(owner[0].userId).toBe(actor); expect(owner[0].role).toBe(0);
  });
}
