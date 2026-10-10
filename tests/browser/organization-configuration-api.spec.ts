import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';

// PRD-27 TC-01/02/03/04/05/07/08/13/14/15: real authenticated HTTP and persisted revisions.
test('PRD-27: private configuration HTTP retains history and original acknowledgments', async ({ context, browser }) => {
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `organization-config-${Date.now()}@example.test`, password: 'configuration-correct-horse-battery', displayName: 'Configuration owner' };
  const owner = await registerNotificationAccount(context.request, account);
  const created = await context.request.post('/organizations', { headers, data: { name: 'Configuration API acceptance' } });
  expect(created.status()).toBe(201); const organization = (await created.json()).organization;
  const path = `/organizations/${organization.id}/configuration`;
  const empty = await context.request.get(path); expect(empty.status()).toBe(200);
  expect((await empty.json())).toEqual({ organizationId: organization.id, version: 0, revision: null });
  expect(empty.headers()['cache-control']).toMatch(/private/); expect(empty.headers()['cache-control']).toMatch(/no-store/);
  const input = { legalName: 'Reviewed original legal name', jurisdiction: 'CA-BC', timezone: 'America/Vancouver',
    corporationIdentifier: `config-${organization.id}`, managementCompanyName: 'Original management', lotCount: 24,
    fiscalYearEndMonth: 12, fiscalYearEndDay: 31, agmCycleMonths: 12, depreciationReportCycleMonths: 36,
    insuranceRenewalDate: '2027-03-15', civicAddress: 'Reviewed civic address',
    emergencyContacts: [{ name: 'Reviewed contact', phone: 'reviewed telephone' }],
    defaultCategories: ['Maintenance'], defaultPriorities: ['Urgent'],
    jurisdictionPolicies: [{ key: 'review_cycle', value: '12', source: 'Administrator supplied', notes: 'Review annually' }] };
  const key = crypto.randomUUID();
  const changeHeaders = { ...headers, 'Idempotency-Key': key, 'X-StrataAI-Expected-Actor': owner.user.id };
  const first = await context.request.patch(path, { headers: changeHeaders, data: { version: 0, configuration: input } });
  expect(first.status()).toBe(200); const original = await first.json();
  expect(original.version).toBe(1); expect(original.actorId).toBe(owner.user.id);
  expect(original.organizationId).toBe(organization.id); expect(original.organizationName).toBe(organization.name);
  expect(original.organizationType).toBe('STRATA'); expect(original.configuration).toMatchObject(input);
  const laterInput = { ...input, jurisdiction: 'CA-ON', timezone: 'America/Toronto', managementCompanyName: 'Later management' };
  const second = await context.request.patch(path, { headers: { ...changeHeaders, 'Idempotency-Key': crypto.randomUUID() },
    data: { version: 1, configuration: laterInput } });
  expect(second.status()).toBe(200); expect((await second.json()).version).toBe(2);
  const retry = await context.request.patch(path, { headers: changeHeaders, data: { version: 0, configuration: input } });
  expect(retry.status()).toBe(200); expect(await retry.json()).toEqual(original);
  const stale = await context.request.patch(path, { headers: { ...changeHeaders, 'Idempotency-Key': crypto.randomUUID() },
    data: { version: 0, configuration: input } });
  expect(stale.status()).toBe(409); expect((await stale.json()).code).toBe('version_conflict');
  const current = await context.request.get(path); expect(current.status()).toBe(200);
  const state = await current.json(); expect(state.version).toBe(2); expect(state.revision.configuration).toMatchObject(laterInput);
  const history = await context.request.get(path + '/history'); expect(history.status()).toBe(200);
  const revisions = await history.json(); expect(revisions.items.map((row: { version: number }) => row.version)).toEqual([2, 1]);
  expect(revisions.items[1]).toEqual(original); expect(revisions.nextBeforeVersion).toBeNull();
  const older = await context.request.get(path + '/history?beforeVersion=2'); expect(older.status()).toBe(200);
  expect((await older.json()).items).toEqual([original]);
  const schemaFailure = await context.request.patch(path, { headers: { ...changeHeaders, 'Idempotency-Key': crypto.randomUUID() },
    data: { version: 2, configuration: { ...input, unexpected: true } } });
  expect(schemaFailure.status()).toBe(400); expect((await schemaFailure.json()).code).toBe('invalid_configuration_request');
  const another = await context.request.post('/organizations', { headers, data: { name: 'Other configuration API acceptance' } });
  expect(another.status()).toBe(201); const otherId = (await another.json()).organization.id;
  const collision = await context.request.patch(`/organizations/${otherId}/configuration`, {
    headers: { ...changeHeaders, 'Idempotency-Key': crypto.randomUUID() }, data: { version: 0,
      configuration: { ...laterInput, jurisdiction: 'ca-on', corporationIdentifier: input.corporationIdentifier.toUpperCase() } } });
  expect(collision.status()).toBe(409); expect((await collision.json()).code).toBe('configuration_identifier_unavailable');
  const outsider = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  try {
    await registerNotificationAccount(outsider.request, { ...account, email: `organization-config-other-${Date.now()}@example.test` });
    for (const uri of [path, path + '/history?beforeVersion=invalid']) {
      const denied = await outsider.request.get(uri); expect(denied.status()).toBe(404);
      expect((await denied.json()).code).toBe('organization_not_found'); expect(await denied.text()).not.toContain(input.legalName);
    }
    const denied = await outsider.request.patch(path, { headers, data: '{invalid' });
    expect(denied.status()).toBe(404); expect((await denied.json()).code).toBe('organization_not_found');
  } finally { await outsider.close(); }
  const retained = await context.request.get(path + '/history'); expect(retained.status()).toBe(200);
  expect((await retained.json()).items).toHaveLength(2);
});
