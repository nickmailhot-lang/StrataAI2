import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfigurationChangeIntent } from './organizationConfigurationClient';
import { parseOrganizationConfiguration } from './organizationConfiguration';
import { completeConfigurationChange, forgetConfigurationChanges, forgetForeignConfigurationChanges,
  restoreConfigurationChange, retainConfigurationChange } from './organizationConfigurationRecovery';

const actor = '11111111-1111-4111-8111-111111111111';
const organization = '22222222-2222-4222-8222-222222222222';
const other = '33333333-3333-4333-8333-333333333333';
const key = '44444444-4444-4444-8444-444444444444';
const storageKey = `strataai:configuration-change:v1:${actor}:${organization}`;
const data = () => parseOrganizationConfiguration({ legalName: 'Reviewed private name', jurisdiction: 'CA-BC', timezone: 'UTC',
  emergencyContacts: [{ name: 'Reviewed contact', phone: '123' }], jurisdictionPolicies: [{ key: 'policy', value: 'value', source: 'source' }] });
const intent = () => ConfigurationChangeIntent.review(organization, actor, 7, data(), key);
afterEach(() => { sessionStorage.clear(); vi.restoreAllMocks(); });
describe('PRD-27 original configuration submission recovery', () => {
  it('restores exact request bytes, reviewed version and key without retaining caller mutations', () => {
    const proposed = data(); const original = ConfigurationChangeIntent.review(organization, actor, 7, proposed, key);
    retainConfigurationChange(sessionStorage, original); proposed.emergencyContacts![0].name = 'Changed';
    const recovered = restoreConfigurationChange(sessionStorage, actor, organization)!;
    expect(recovered.recoveryRecord()).toEqual(original.recoveryRecord()); expect(recovered.expectedVersion).toBe(7);
    expect(recovered.reviewedConfiguration().emergencyContacts![0].name).toBe('Reviewed contact');
    expect(restoreConfigurationChange(sessionStorage, other, organization)).toBeUndefined();
    expect(restoreConfigurationChange(sessionStorage, actor, other)).toBeUndefined();
  });
  it('refuses replacing an unresolved original and retains it after an unrelated completion attempt', () => {
    const original = intent(); retainConfigurationChange(sessionStorage, original);
    const replacement = ConfigurationChangeIntent.review(organization, actor, 8, data());
    expect(() => retainConfigurationChange(sessionStorage, replacement)).toThrow('original');
    expect(() => completeConfigurationChange(sessionStorage, replacement)).toThrow('original');
    expect(restoreConfigurationChange(sessionStorage, actor, organization)!.key).toBe(key);
    completeConfigurationChange(sessionStorage, original); expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
  it.each([
    (record: ReturnType<ConfigurationChangeIntent['recoveryRecord']>) => ({ ...record, actorId: other }),
    (record: ReturnType<ConfigurationChangeIntent['recoveryRecord']>) => ({ ...record, organizationId: other }),
    (record: ReturnType<ConfigurationChangeIntent['recoveryRecord']>) => ({ ...record, body: record.body + ' ' }),
    (record: ReturnType<ConfigurationChangeIntent['recoveryRecord']>) => ({ ...record, body: '{"version":7,"version":7,"configuration":{}}' }),
    (record: ReturnType<ConfigurationChangeIntent['recoveryRecord']>) => ({ ...record, extra: 'Unexpected private value' }),
  ])('withholds invalid or substituted saved records without discarding or overwriting them', change => {
    const encoded = JSON.stringify(change(intent().recoveryRecord())); sessionStorage.setItem(storageKey, encoded);
    expect(() => restoreConfigurationChange(sessionStorage, actor, organization)).toThrow();
    expect(() => retainConfigurationChange(sessionStorage, intent())).toThrow();
    expect(sessionStorage.getItem(storageKey)).toBe(encoded);
  });
  it('refuses new originals when the bounded recovery capacity is full without evicting existing work', () => {
    for (let index = 0; index < 32; index++) sessionStorage.setItem(`strataai:configuration-change:v1:existing:${index}`, 'retained');
    expect(() => retainConfigurationChange(sessionStorage, intent())).toThrow(); expect(sessionStorage.length).toBe(32);
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
  it('detects failed storage writes and reads', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {});
    expect(() => retainConfigurationChange(sessionStorage, intent())).toThrow();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('Private storage diagnostic'); });
    expect(() => restoreConfigurationChange(sessionStorage, actor, organization)).toThrow('No new change was sent');
  });
  it('removes only configuration recovery on sign-out and only foreign configuration scopes on account replacement', () => {
    retainConfigurationChange(sessionStorage, intent()); sessionStorage.setItem('unrelated', 'keep');
    forgetForeignConfigurationChanges(sessionStorage, other);
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(sessionStorage.getItem('unrelated')).toBe('keep');
    retainConfigurationChange(sessionStorage, intent()); forgetConfigurationChanges();
    expect(sessionStorage.getItem(storageKey)).toBeNull(); expect(sessionStorage.getItem('unrelated')).toBe('keep');
  });
});
