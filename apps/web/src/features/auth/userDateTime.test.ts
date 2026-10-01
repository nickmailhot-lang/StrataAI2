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
});
