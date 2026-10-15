import type { APIRequestContext, Locator } from '@playwright/test';
import { expect } from './releaseTest';

// Keep the original event instant separate from its viewer-specific display.
export async function expectConfigurationActivityTime(activity: Locator, request: APIRequestContext,
  source: { eventId: string; updatedAt: string }) {
  const row = activity.getByText(`Original event reference: ${source.eventId}`, { exact: true }).locator('..');
  const time = row.locator('time');
  await expect(time).toHaveCount(1);
  await expect(time).toBeVisible();
  await expect(time).toHaveAttribute('datetime', source.updatedAt);
  const response = await request.get('/me');
  expect(response.status()).toBe(200);
  const profile = await response.json();
  expect(typeof profile.locale).toBe('string'); expect(typeof profile.timezone).toBe('string');
  const displayed = await activity.page().evaluate(({ instant, locale, timezone }) =>
    new Intl.DateTimeFormat(locale, { timeZone: timezone, year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short' }).format(new Date(instant)),
  { instant: source.updatedAt, locale: profile.locale, timezone: profile.timezone });
  await expect(time).toHaveText(displayed);
}
