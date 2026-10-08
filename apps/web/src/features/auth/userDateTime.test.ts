import { formatUserDateTime } from './userDateTime';

describe('AUTH-FR-010 profile date display', () => {
  it('uses the saved timezone across midnight rather than the browser timezone', () => {
    const instant = '2026-03-08T00:30:00Z';
    expect(formatUserDateTime(instant, { locale: 'en-US', timezone: 'UTC' })).toContain('Mar 8, 2026');
    expect(formatUserDateTime(instant, { locale: 'en-US', timezone: 'America/Vancouver' })).toContain('Mar 7, 2026');
    expect(formatUserDateTime(instant, { locale: 'en-US', timezone: 'America/Vancouver' })).toContain('16:30');
  });
  it('applies daylight saving transitions using the timezone database', () => {
    const preferences = { locale: 'en-CA', timezone: 'America/Vancouver' };
    expect(formatUserDateTime('2026-03-08T09:30:00Z', preferences)).toContain('01:30');
    expect(formatUserDateTime('2026-03-08T10:30:00Z', preferences)).toContain('03:30');
  });
  it('does not silently fall back to a browser timezone for invalid data', () => {
    expect(formatUserDateTime('invalid', { locale: 'en-CA', timezone: 'UTC' })).toBeUndefined();
    expect(formatUserDateTime('2026-03-08T00:30:00', { locale: 'en-CA', timezone: 'UTC' })).toBeUndefined();
    expect(formatUserDateTime('2026-03-08T00:30:00Z', { locale: 'en-CA', timezone: 'Not/AZone' })).toBeUndefined();
  });
  it.each([
    '2026-02-30T12:00:00Z', '2026-02-29T12:00:00+05:30',
    '2026-04-31T12:00:00-07:00', '2026-03-08T24:00:00Z',
  ])('does not normalize an impossible source calendar: %s', value => {
    expect(formatUserDateTime(value, { locale: 'en-US', timezone: 'UTC' })).toBeUndefined();
  });
  it('validates the source calendar before converting its explicit offset', () => {
    const preferences = { locale: 'en-US', timezone: 'UTC' };
    expect(formatUserDateTime('2028-02-29T00:30:00.1234567+14:00', preferences))
      .toBe(formatUserDateTime('2028-02-28T10:30:00.1234567Z', preferences));
    expect(formatUserDateTime('2028-02-29T23:30:00-14:00', preferences))
      .toBe(formatUserDateTime('2028-03-01T13:30:00Z', preferences));
    expect(formatUserDateTime('2028-02-29T00:30:00Z', preferences)).toContain('Feb 29, 2028');
  });
});
