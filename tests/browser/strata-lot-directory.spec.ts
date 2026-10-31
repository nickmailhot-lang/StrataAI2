import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';

const headers = { 'X-StrataAI-Request': '1' };
for (const width of [320, 390, 768, 1280]) {
  test(`PRD-28-TC-01/07/11/12 LOT-FR-001/002/005/006: unit directory create, correct, history and return at ${width}px`, async ({ page, context }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await registerNotificationAccount(context.request, { email: `unit-directory-${width}-${Date.now()}@example.test`, password: 'unit-directory-correct-horse', displayName: 'Unit administrator' });
    const created = await context.request.post('/organizations', { headers, data: { name: 'Unit directory acceptance' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
    await page.goto(`/app/${organization}`);
    await page.getByText('Organization actions', { exact: true }).click();
    await page.getByRole('link', { name: 'Unit directory', exact: true }).click();
    await expect(page.getByText(/No units have been recorded/)).toBeVisible();
    await page.getByRole('button', { name: 'Add unit', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Add unit', exact: true })).toBeFocused();
    await page.getByLabel('Unit identifier', { exact: false }).fill('101');
    await page.getByLabel('Lot identifier', { exact: false }).fill('SL1');
    await page.getByLabel('Building', { exact: true }).fill('North');
    const attempts: { key: string | undefined; body: string | null }[] = [];
    await page.route(`**/organizations/${organization}/strata-lots/*`, async route => {
      if (route.request().method() !== 'PATCH') return route.continue();
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      const result = await route.fetch(); expect(result.status()).toBe(200);
      if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
    });
    const review = page.getByRole('dialog', { name: 'Review unit change', exact: true });
    const launcher = page.getByRole('button', { name: 'Review unit change', exact: true });
    await expect(async () => {
      await openAdmittedReview(launcher, review);
      await expect(review.getByRole('button', { name: 'Return to draft' })).toBeFocused({ timeout: 500 });
      await expect(review.getByRole('button', { name: 'Confirm unit change' })).toBeEnabled({ timeout: 500 });
      await page.keyboard.press('Tab');
      await expect(review.getByRole('button', { name: 'Confirm unit change' })).toBeFocused({ timeout: 500 });
    }).toPass({ timeout: 5_000 });
    await pressObservedRequest(review.getByRole('button', { name: 'Confirm unit change' }), () => attempts.length, 1);
    await expect(page.getByRole('button', { name: 'Retry original unit change' })).toBeEnabled();
    await page.reload(); await pressObservedRequest(page.getByRole('button', { name: 'Retry original unit change' }), () => attempts.length, 2);
    await expect(page.getByText('Change acknowledged. Current unit revision 1.', { exact: true })).toBeVisible();
    expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
    const directory = await context.request.get(`/organizations/${organization}/strata-lots`);
    expect(directory.status()).toBe(200); const rows = (await directory.json()).items; expect(rows).toHaveLength(1); const lot = rows[0];
    await page.getByLabel('Unit identifier', { exact: false }).fill('101A');
    await expect(async () => {
      const status = page.getByLabel('Unit status', { exact: true });
      if ((await status.textContent())?.includes('Inactive')) return;
      await status.click({ timeout: 500 }); await page.getByRole('option', { name: 'Inactive', exact: true }).click({ timeout: 500 });
      await expect(status).toHaveText('Inactive', { timeout: 500 });
    }).toPass({ timeout: 5_000 });
    await openAdmittedReview(launcher, review);
    await pressObservedRequest(review.getByRole('button', { name: 'Confirm unit change', exact: true }), () => attempts.length, 3);
    await expect(page.getByText('Change acknowledged. Current unit revision 2.', { exact: true })).toBeVisible();
    await page.reload(); await page.getByRole('button', { name: 'Unit 101A · Lot SL1', exact: true }).click();
    await expect(page.getByLabel('Unit identifier', { exact: false })).toHaveValue('101A');
    const history = page.getByRole('region', { name: 'Unit history', exact: true });
    await expect(async () => {
      if (await history.isVisible()) return;
      await page.getByRole('button', { name: 'View unit history' }).click({ timeout: 500 });
      await expect(history).toBeVisible({ timeout: 500 });
    }).toPass({ timeout: 5_000 });
    await expect(history.getByRole('heading', { name: 'Revision 2', exact: true })).toBeVisible();
    await expect(history.getByRole('heading', { name: 'Revision 1', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Unit history', exact: true })).toBeFocused();
    await expect(page.getByRole('heading', { name: 'Unit history', exact: true })).toBeInViewport();
    const current = await context.request.get(`/organizations/${organization}/strata-lots/${lot.id}`);
    expect(current.status()).toBe(200); expect(await current.json()).toMatchObject({ id: lot.id, version: 2, data: { unitIdentifier: '101A', status: 'INACTIVE' } });
    const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
    expect(accessibility.violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    // Capture from the document origin so fixed shell controls do not appear
    // halfway down a full-page image after history's deliberate focus scroll.
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: testInfo.outputPath(`unit-directory-${width}.png`), fullPage: true });
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByRole('button', { name: 'Close unit editor' }).click();
    await page.getByRole('link', { name: 'Return to Organization' }).click(); await expect(page).toHaveURL(`/app/${organization}`);
  });
}

for (const originalVersion of [0, 1]) {
  test(`PRD-28-TC-07/08/16: restored version ${originalVersion} compares current values before an explicit fresh change`, async ({ page, context }) => {
    await page.setViewportSize({ width: 390, height: 900 });
    await registerNotificationAccount(context.request, { email: `unit-conflict-${originalVersion}-${Date.now()}@example.test`, password: 'unit-directory-correct-horse', displayName: 'Unit administrator' });
    const created = await context.request.post('/organizations', { headers, data: { name: 'Unit recovery acceptance' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
    const data = { unitIdentifier: '101', lotIdentifier: 'SL1', civicAddress: null, building: null, floor: null, type: null, status: 'ACTIVE' };
    let lotId: string = randomUUID();
    if (originalVersion === 1) {
      expect((await context.request.patch(`/organizations/${organization}/strata-lots/${lotId}`, {
        headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { version: 0, data },
      })).status()).toBe(200);
    }
    const attempts: { lotId: string; key: string | undefined; body: string | null }[] = [];
    await page.route(`**/organizations/${organization}/strata-lots/*`, async route => {
      if (route.request().method() !== 'PATCH') return route.continue();
      const attempt = attempts.push({ lotId: new URL(route.request().url()).pathname.split('/').at(-1)!, key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      if (attempt === 1) return route.abort('failed');
      const response = await route.fetch(); expect(response.status()).toBe(attempt === 2 ? 409 : 200);
      await route.fulfill({ response });
    });
    await page.goto(`/app/${organization}/property/units`);
    await page.getByRole('button', { name: originalVersion ? 'Unit 101 · Lot SL1' : 'Add unit', exact: true }).click();
    await page.getByLabel('Unit identifier', { exact: false }).fill('101A');
    if (!originalVersion) await page.getByLabel('Lot identifier', { exact: false }).fill('SL1');
    const review = page.getByRole('dialog', { name: 'Review unit change', exact: true });
    const launcher = page.getByRole('button', { name: 'Review unit change', exact: true });
    await openAdmittedReview(launcher, review);
    await pressObservedRequest(review.getByRole('button', { name: 'Confirm unit change', exact: true }), () => attempts.length, 1);
    await expect(page.getByRole('button', { name: 'Retry original unit change' })).toBeEnabled();
    lotId = attempts[0].lotId; expect(lotId).toMatch(/^[0-9a-f-]{36}$/);
    const competing = await context.request.patch(`/organizations/${organization}/strata-lots/${lotId}`, {
      headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { version: originalVersion, data: { ...data, unitIdentifier: '101B' } },
    });
    expect(competing.status()).toBe(200);
    await page.reload();
    await expect(page.getByLabel('Unit identifier', { exact: false })).toBeDisabled(); expect(attempts).toHaveLength(1);
    await pressObservedRequest(page.getByRole('button', { name: 'Retry original unit change' }), () => attempts.length, 2);
    await expect(page.getByText(/Your draft is preserved. Refresh current records/)).toBeVisible();
    await expect(page.getByRole('region', { name: 'Unit editor', exact: true })).toBeVisible();
    expect(attempts[1]).toEqual(attempts[0]);
    await page.getByRole('button', { name: 'Refresh current records', exact: true }).click();
    await expect(page.getByRole('heading', { name: `Unit 101B · revision ${originalVersion + 1}`, exact: true })).toBeVisible();
    await expect(page.getByLabel('Unit identifier', { exact: false })).toHaveValue('101A'); expect(attempts).toHaveLength(2);
    await page.getByRole('button', { name: 'Keep draft after review', exact: true }).click(); expect(attempts).toHaveLength(2);
    await openAdmittedReview(launcher, review);
    await pressObservedRequest(review.getByRole('button', { name: 'Confirm unit change', exact: true }), () => attempts.length, 3);
    await expect(page.getByText(`Change acknowledged. Current unit revision ${originalVersion + 2}.`, { exact: true })).toBeVisible();
    expect(attempts[2].key).not.toBe(attempts[0].key); expect(JSON.parse(attempts[2].body!).version).toBe(originalVersion + 1);
    const current = await context.request.get(`/organizations/${organization}/strata-lots/${lotId}`);
    expect(current.status()).toBe(200); expect(await current.json()).toMatchObject({ id: lotId, version: originalVersion + 2, data: { unitIdentifier: '101A' } });
    const directory = await context.request.get(`/organizations/${organization}/strata-lots`);
    expect(directory.status()).toBe(200); expect((await directory.json()).items).toHaveLength(1);
    await page.getByRole('button', { name: 'Close unit editor', exact: true }).click();
    await page.getByRole('link', { name: 'Return to Organization', exact: true }).click(); await expect(page).toHaveURL(`/app/${organization}`);
  });
}

test('PRD-28-TC-01 LOT-FR-006: directory and history continue past 50 without losing references', async ({ page, context }) => {
  await registerNotificationAccount(context.request, { email: `unit-pages-${Date.now()}@example.test`, password: 'unit-directory-correct-horse', displayName: 'Directory administrator' });
  const created = await context.request.post('/organizations', { headers, data: { name: 'Unit pages acceptance' } });
  expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
  const ids = Array.from({ length: 55 }, () => randomUUID()).sort();
  const data = { unitIdentifier: 'Unit 0', lotIdentifier: 'Lot 0', civicAddress: null, building: null, floor: null, type: null, status: 'ACTIVE' };
  for (let index = 0; index < ids.length; index++) {
    const response = await context.request.patch(`/organizations/${organization}/strata-lots/${ids[index]}`, {
      headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { version: 0, data: { ...data, unitIdentifier: `Unit ${index}`, lotIdentifier: `Lot ${index}` } },
    }); expect(response.status()).toBe(200);
  }
  for (let version = 1; version <= 51; version++) {
    const response = await context.request.patch(`/organizations/${organization}/strata-lots/${ids[0]}`, {
      headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { version, data: { ...data, building: `Revision ${version + 1}` } },
    }); expect(response.status()).toBe(200);
  }
  await page.goto(`/app/${organization}/property/units`);
  await expect(page.getByRole('button', { name: /^Unit Unit / })).toHaveCount(50);
  await page.getByRole('button', { name: 'Next directory page' }).click();
  await expect(page.getByRole('button', { name: /^Unit Unit / })).toHaveCount(5);
  await expect(page.getByRole('button', { name: 'Next directory page' })).toBeDisabled();
  await page.getByRole('button', { name: 'First directory page' }).click();
  await page.getByRole('button', { name: 'Unit Unit 0 · Lot Lot 0', exact: true }).click();
  await page.getByRole('button', { name: 'View unit history' }).click();
  await expect(page.getByRole('heading', { name: 'Revision 52', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Earlier unit history' }).click();
  await expect(page.getByRole('heading', { name: 'Revision 1', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Earlier unit history' })).toBeDisabled();
});
