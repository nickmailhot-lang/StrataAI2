# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: strata-lot-directory.spec.ts >> PRD-28-TC-01/07/11/12 LOT-FR-001/002/005/006: unit directory create, correct, history and return at 320px
- Location: tests/browser/strata-lot-directory.spec.ts:9:7

# Error details

```
Error: expect(locator).toBeFocused() failed

Locator:  getByRole('heading', { name: 'Unit history', exact: true })
Expected: focused
Received: inactive
Timeout:  5000ms

Call log:
  - Expect "toBeFocused" getByRole('heading', { name: 'Unit history', exact: true }) with timeout 5000ms
  - waiting for getByRole('heading', { name: 'Unit history', exact: true })
    14 × locator resolved to <h2 tabindex="-1" id="unit-history-title" class="MuiTypography-root MuiTypography-h5 css-1edffmg">Unit history</h2>
       - unexpected value "inactive"

```

```yaml
- heading "Unit history" [level=2]
```

# Test source

```ts
  1   | import AxeBuilder from '@axe-core/playwright';
  2   | import { randomUUID } from 'node:crypto';
  3   | import { expect, test } from './releaseTest';
  4   | import { registerNotificationAccount } from './notificationAccountFixture';
  5   | import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';
  6   | 
  7   | const headers = { 'X-StrataAI-Request': '1' };
  8   | for (const width of [320, 390, 768, 1280]) {
  9   |   test(`PRD-28-TC-01/07/11/12 LOT-FR-001/002/005/006: unit directory create, correct, history and return at ${width}px`, async ({ page, context }, testInfo) => {
  10  |     await page.setViewportSize({ width, height: 900 });
  11  |     await registerNotificationAccount(context.request, { email: `unit-directory-${width}-${Date.now()}@example.test`, password: 'unit-directory-correct-horse', displayName: 'Unit administrator' });
  12  |     const created = await context.request.post('/organizations', { headers, data: { name: 'Unit directory acceptance' } });
  13  |     expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
  14  |     await page.goto(`/app/${organization}`);
  15  |     await page.getByText('Organization actions', { exact: true }).click();
  16  |     await page.getByRole('link', { name: 'Unit directory', exact: true }).click();
  17  |     await expect(page.getByText(/No units have been recorded/)).toBeVisible();
  18  |     await page.getByRole('button', { name: 'Add unit', exact: true }).click();
  19  |     await expect(page.getByRole('heading', { name: 'Add unit', exact: true })).toBeFocused();
  20  |     await page.getByLabel('Unit identifier', { exact: false }).fill('101');
  21  |     await page.getByLabel('Lot identifier', { exact: false }).fill('SL1');
  22  |     await page.getByLabel('Building', { exact: true }).fill('North');
  23  |     const attempts: { key: string | undefined; body: string | null }[] = [];
  24  |     await page.route(`**/organizations/${organization}/strata-lots/*`, async route => {
  25  |       if (route.request().method() !== 'PATCH') return route.continue();
  26  |       attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
  27  |       const result = await route.fetch(); expect(result.status()).toBe(200);
  28  |       if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
  29  |     });
  30  |     const review = page.getByRole('dialog', { name: 'Review unit change', exact: true });
  31  |     const launcher = page.getByRole('button', { name: 'Review unit change', exact: true });
  32  |     await expect(async () => {
  33  |       await openAdmittedReview(launcher, review);
  34  |       await expect(review.getByRole('button', { name: 'Return to draft' })).toBeFocused({ timeout: 500 });
  35  |       await expect(review.getByRole('button', { name: 'Confirm unit change' })).toBeEnabled({ timeout: 500 });
  36  |       await page.keyboard.press('Tab');
  37  |       await expect(review.getByRole('button', { name: 'Confirm unit change' })).toBeFocused({ timeout: 500 });
  38  |     }).toPass({ timeout: 5_000 });
  39  |     await pressObservedRequest(review.getByRole('button', { name: 'Confirm unit change' }), () => attempts.length, 1);
  40  |     await expect(page.getByRole('button', { name: 'Retry original unit change' })).toBeEnabled();
  41  |     await page.reload(); await pressObservedRequest(page.getByRole('button', { name: 'Retry original unit change' }), () => attempts.length, 2);
  42  |     await expect(page.getByText('Change acknowledged. Current unit revision 1.', { exact: true })).toBeVisible();
  43  |     expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
  44  |     const directory = await context.request.get(`/organizations/${organization}/strata-lots`);
  45  |     expect(directory.status()).toBe(200); const rows = (await directory.json()).items; expect(rows).toHaveLength(1); const lot = rows[0];
  46  |     await page.getByLabel('Unit identifier', { exact: false }).fill('101A');
  47  |     await expect(async () => {
  48  |       const status = page.getByLabel('Unit status', { exact: true });
  49  |       if ((await status.textContent())?.includes('Inactive')) return;
  50  |       await status.click({ timeout: 500 }); await page.getByRole('option', { name: 'Inactive', exact: true }).click({ timeout: 500 });
  51  |       await expect(status).toHaveText('Inactive', { timeout: 500 });
  52  |     }).toPass({ timeout: 5_000 });
  53  |     await openAdmittedReview(launcher, review);
  54  |     await pressObservedRequest(review.getByRole('button', { name: 'Confirm unit change', exact: true }), () => attempts.length, 3);
  55  |     await expect(page.getByText('Change acknowledged. Current unit revision 2.', { exact: true })).toBeVisible();
  56  |     await page.reload(); await page.getByRole('button', { name: 'Unit 101A · Lot SL1', exact: true }).click();
  57  |     await expect(page.getByLabel('Unit identifier', { exact: false })).toHaveValue('101A');
  58  |     const history = page.getByRole('region', { name: 'Unit history', exact: true });
  59  |     await expect(async () => {
  60  |       if (await history.isVisible()) return;
  61  |       await page.getByRole('button', { name: 'View unit history' }).click({ timeout: 500 });
  62  |       await expect(history).toBeVisible({ timeout: 500 });
  63  |     }).toPass({ timeout: 5_000 });
  64  |     await expect(history.getByRole('heading', { name: 'Revision 2', exact: true })).toBeVisible();
  65  |     await expect(history.getByRole('heading', { name: 'Revision 1', exact: true })).toBeVisible();
> 66  |     await expect(page.getByRole('heading', { name: 'Unit history', exact: true })).toBeFocused();
      |                                                                                    ^ Error: expect(locator).toBeFocused() failed
  67  |     const current = await context.request.get(`/organizations/${organization}/strata-lots/${lot.id}`);
  68  |     expect(current.status()).toBe(200); expect(await current.json()).toMatchObject({ id: lot.id, version: 2, data: { unitIdentifier: '101A', status: 'INACTIVE' } });
  69  |     const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
  70  |     expect(accessibility.violations).toEqual([]);
  71  |     expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  72  |     // Capture from the document origin so fixed shell controls do not appear
  73  |     // halfway down a full-page image after history's deliberate focus scroll.
  74  |     await page.evaluate(() => window.scrollTo(0, 0));
  75  |     await page.screenshot({ path: testInfo.outputPath(`unit-directory-${width}.png`), fullPage: true });
  76  |     await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
  77  |     expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  78  |     await page.getByRole('button', { name: 'Close unit editor' }).click();
  79  |     await page.getByRole('link', { name: 'Return to Organization' }).click(); await expect(page).toHaveURL(`/app/${organization}`);
  80  |   });
  81  | }
  82  | 
  83  | test('PRD-28-TC-01 LOT-FR-006: directory and history continue past 50 without losing references', async ({ page, context }) => {
  84  |   await registerNotificationAccount(context.request, { email: `unit-pages-${Date.now()}@example.test`, password: 'unit-directory-correct-horse', displayName: 'Directory administrator' });
  85  |   const created = await context.request.post('/organizations', { headers, data: { name: 'Unit pages acceptance' } });
  86  |   expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
  87  |   const ids = Array.from({ length: 55 }, () => randomUUID()).sort();
  88  |   const data = { unitIdentifier: 'Unit 0', lotIdentifier: 'Lot 0', civicAddress: null, building: null, floor: null, type: null, status: 'ACTIVE' };
  89  |   for (let index = 0; index < ids.length; index++) {
  90  |     const response = await context.request.patch(`/organizations/${organization}/strata-lots/${ids[index]}`, {
  91  |       headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { version: 0, data: { ...data, unitIdentifier: `Unit ${index}`, lotIdentifier: `Lot ${index}` } },
  92  |     }); expect(response.status()).toBe(200);
  93  |   }
  94  |   for (let version = 1; version <= 51; version++) {
  95  |     const response = await context.request.patch(`/organizations/${organization}/strata-lots/${ids[0]}`, {
  96  |       headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { version, data: { ...data, building: `Revision ${version + 1}` } },
  97  |     }); expect(response.status()).toBe(200);
  98  |   }
  99  |   await page.goto(`/app/${organization}/property/units`);
  100 |   await expect(page.getByRole('button', { name: /^Unit Unit / })).toHaveCount(50);
  101 |   await page.getByRole('button', { name: 'Next directory page' }).click();
  102 |   await expect(page.getByRole('button', { name: /^Unit Unit / })).toHaveCount(5);
  103 |   await expect(page.getByRole('button', { name: 'Next directory page' })).toBeDisabled();
  104 |   await page.getByRole('button', { name: 'First directory page' }).click();
  105 |   await page.getByRole('button', { name: 'Unit Unit 0 · Lot Lot 0', exact: true }).click();
  106 |   await page.getByRole('button', { name: 'View unit history' }).click();
  107 |   await expect(page.getByRole('heading', { name: 'Revision 52', exact: true })).toBeVisible();
  108 |   await page.getByRole('button', { name: 'Earlier unit history' }).click();
  109 |   await expect(page.getByRole('heading', { name: 'Revision 1', exact: true })).toBeVisible();
  110 |   await expect(page.getByRole('button', { name: 'Earlier unit history' })).toBeDisabled();
  111 | });
  112 | 
```