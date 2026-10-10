import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';

for (const width of [1280, 390]) {
  test(`PRD-27: explicit configuration review, owning intake, refresh and history at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await registerNotificationAccount(context.request, { email: `organization-configuration-ui-${width}-${Date.now()}@example.test`,
      password: 'configuration-review-correct-horse', displayName: 'Configuration administrator' });
    const headers = { 'X-StrataAI-Request': '1' };
    const created = await context.request.post('/organizations', { headers, data: { name: 'Configuration browser acceptance' } });
    expect(created.status()).toBe(201); const organizationId = (await created.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId, name: 'Reviewed intake Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const boardId = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${boardId}/lists`, { headers, data: { name: 'Reviewed intake List' } });
    expect(listReply.status()).toBe(201); const listId = (await listReply.json()).id;
    await page.goto(`/app/${organizationId}/configuration`);
    await expect(page.getByText(/No configuration has been recorded/)).toBeVisible();
    await expect(page.getByLabel(/^Legal name/)).toHaveValue('');
    for (const [label, value] of [
      ['Legal name', 'Reviewed legal name'], ['Jurisdiction', 'CA-BC'], ['Organization timezone (IANA)', 'America/Vancouver'],
      ['Corporation or registration identifier', `ui-${organizationId}`], ['Civic address', 'Reviewed civic address'],
      ['Management company', 'Reviewed manager'], ['Insurance renewal date', '2027-03-15'], ['Lot count', '24'],
      ['Fiscal year end month', '12'], ['Fiscal year end day', '31'], ['AGM cycle (months)', '12'], ['Depreciation report cycle (months)', '36'],
    ]) await page.getByLabel(new RegExp('^' + label.replace(/[()]/g, '\\$&'))).fill(value);
    await page.getByRole('button', { name: 'Add emergency contact' }).click();
    await page.getByLabel('Emergency contact 1 name').fill('Reviewed contact');
    await page.getByLabel('Emergency contact 1 phone').fill('Reviewed telephone');
    await page.getByRole('button', { name: 'Add category', exact: true }).click(); await page.getByLabel('Category 1').fill('Maintenance');
    await page.getByRole('button', { name: 'Add priority', exact: true }).click(); await page.getByLabel('Priority 1').fill('Urgent');
    await page.getByRole('button', { name: 'Add jurisdiction policy' }).click();
    for (const [label, value] of [['Policy 1 key', 'review_cycle'], ['Policy 1 value', '12'], ['Policy 1 source', 'Administrator supplied'], ['Policy 1 notes', 'Review annually']])
      await page.getByLabel(label).fill(value);
    await page.getByRole('combobox', { name: 'Intake Board', exact: true }).click();
    await page.getByRole('option', { name: 'Reviewed intake Board', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Intake List', exact: true })).toBeEnabled();
    await page.getByRole('combobox', { name: 'Intake List', exact: true }).click();
    await page.getByRole('option', { name: 'Reviewed intake List', exact: true }).click();
    const review = page.getByRole('button', { name: 'Review configuration change', exact: true });
    await review.focus(); await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Review configuration change', exact: true }); await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Return to draft' })).toBeFocused();
    for (const value of ['Reviewed legal name', 'Reviewed manager', 'Reviewed intake Board', 'Reviewed intake List', 'Source: Administrator supplied', 'Notes: Review annually'])
      await expect(dialog.getByText(value, { exact: true })).toBeVisible();
    await page.keyboard.press('Tab'); await expect(dialog.getByRole('button', { name: 'Approve configuration change' })).toBeFocused();
    await page.keyboard.press('Enter'); await expect(dialog).not.toBeVisible();
    await expect(page.getByText('Change acknowledged. Current configuration revision 1.', { exact: true })).toBeVisible();
    await expect(review).toBeFocused();
    const current = await context.request.get(`/organizations/${organizationId}/configuration`); expect(current.status()).toBe(200);
    expect((await current.json()).revision.configuration).toMatchObject({ legalName: 'Reviewed legal name', lotCount: 24, fiscalYearEndMonth: 12, fiscalYearEndDay: 31,
      agmCycleMonths: 12, depreciationReportCycleMonths: 36, insuranceRenewalDate: '2027-03-15', intakeBoardId: boardId, intakeListId: listId,
      emergencyContacts: [{ name: 'Reviewed contact', phone: 'Reviewed telephone' }], defaultCategories: ['Maintenance'], defaultPriorities: ['Urgent'],
      jurisdictionPolicies: [{ key: 'review_cycle', value: '12', source: 'Administrator supplied', notes: 'Review annually' }] });
    await page.reload(); await expect(page.getByLabel(/^Legal name/)).toHaveValue('Reviewed legal name');
    await page.getByRole('button', { name: 'View configuration history' }).click();
    const history = page.getByRole('region', { name: 'Configuration history' }); await expect(history.getByRole('heading', { name: 'Revision 1', exact: true })).toBeVisible();
    await expect(history.getByText('Source: Administrator supplied', { exact: true })).toBeVisible();
    await expect(history.getByRole('heading', { name: 'Configuration history', exact: true })).toBeFocused();
  });
}
