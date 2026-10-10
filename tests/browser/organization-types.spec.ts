import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';

const classifications = [
  ['STRATA', 'Strata'], ['HOA', 'Homeowners association'], ['CONDOMINIUM', 'Condominium'],
  ['COOPERATIVE', 'Cooperative'], ['PROPERTY_MANAGEMENT_COMPANY', 'Property management company'],
  ['GENERIC', 'General organization'],
] as const;

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-27: all Organization classifications persist after creation and reload at ${viewport.width}px`, async ({ page, context, browser, baseURL }) => {
    test.setTimeout(180_000);
    await page.setViewportSize(viewport);
    const suffix = crypto.randomUUID();
    const account = await registerNotificationAccount(context.request, {
      email: `organization-type-${suffix}@example.test`, password: 'organization-type-correct-horse', displayName: 'Organization owner',
    });
    const outsider = await browser.newContext({ baseURL });
    try {
      await registerNotificationAccount(outsider.request, {
        email: `organization-type-outsider-${suffix}@example.test`, password: 'organization-type-correct-horse', displayName: 'Other account',
      });
      const created: string[] = [];
      for (const [type, label] of classifications) {
        await page.goto('/app');
        const opener = page.getByRole('button', { name: 'Create organization', exact: true });
        await opener.focus(); await opener.press('Enter');
        await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toBeFocused();
        const selector = page.getByRole('combobox', { name: 'Organization type', exact: true });
        await expect(selector).toHaveText('Strata');
        await selector.focus(); await selector.press('Enter');
        await page.keyboard.press('Home');
        for (let index = 0; index < classifications.findIndex(([code]) => code === type); index++)
          await page.keyboard.press('ArrowDown');
        await page.keyboard.press('Enter');
        await expect(selector).toHaveText(label);
        await page.getByRole('textbox', { name: 'Name', exact: true }).fill(`Organization ${type}`);
        await page.getByRole('button', { name: 'Create', exact: true }).focus();
        const acknowledgment = page.waitForResponse(response => new URL(response.url()).pathname === '/organizations'
          && response.request().method() === 'POST');
        await page.keyboard.press('Enter');
        const receipt = await acknowledgment;
        expect(receipt.status()).toBe(201);
        const original = await receipt.json(); const id = original.organization.id;
        expect(original.organization).toMatchObject({ type, ownerUserId: account.user.id, version: 1 });
        expect(original.role).toBe(0); created.push(id);
        await expect(page).toHaveURL(new RegExp(`/app/${id}$`));
        await expect(page.getByText(`Organization type: ${label}`, { exact: true })).toBeVisible();
        await page.reload();
        await expect(page.getByText(`Organization type: ${label}`, { exact: true })).toBeVisible();
        const canonical = await context.request.get(`/organizations/${id}`);
        expect(canonical.status()).toBe(200); expect(await canonical.json()).toEqual(original);
        expect((await outsider.request.get(`/organizations/${id}`)).status()).toBe(404);
        const members = await context.request.get(`/organizations/${id}/members`);
        expect(members.status()).toBe(200);
        const rows = (await members.json()).items;
        expect(rows).toHaveLength(1); expect(rows[0]).toMatchObject({ userId: account.user.id, role: 0 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      }
      const directory = await context.request.get('/organizations/directory'); expect(directory.status()).toBe(200);
      const rows = (await directory.json()).items;
      expect(rows).toHaveLength(classifications.length);
      expect(rows.map((row: { organization: { id: string } }) => row.organization.id).sort()).toEqual(created.sort());
      expect(rows.map((row: { organization: { type: string } }) => row.organization.type).sort())
        .toEqual(classifications.map(([type]) => type).sort());
    } finally { await outsider.close(); }
  });
}
