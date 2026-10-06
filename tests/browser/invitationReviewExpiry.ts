import type { Request } from '@playwright/test';
import { expect, type Page } from './releaseTest';

export async function exerciseInvitationReviewExpiry(page: Page, fixture: {
  id: string; token: string; organizationName: string; boardName?: string;
}) {
  const before = await page.request.get('/me/invitations'); expect(before.status()).toBe(200);
  const pending = await before.json();
  const invitation = pending.items.find((row: { id: string }) => row.id === fixture.id);
  expect(invitation).toBeDefined(); const expires = Date.parse(invitation.expiresAt);
  const originalTime = await page.evaluate(() => Date.now());
  let writes = 0;
  const observe = (request: Request) => {
    if (request.method() === 'POST' && new URL(request.url()).pathname === `/me/invitations/${fixture.id}/accept`) writes++;
  };
  page.on('request', observe);
  try {
    // Only browser time advances around the persisted expiry. The API and its
    // existing proof fixture remain unchanged; this is not server-expiry proof.
    await page.clock.install({ time: new Date(expires - 2000) }); await page.clock.pauseAt(new Date(expires - 1000));
    await page.goto(`/invitation#token=${fixture.token}`); await expect(page).toHaveURL(/\/invitation$/);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Accept reviewed invitation' })).toBeVisible();
    await page.clock.runFor(1500);
    await expect(page.getByText('The reviewed invitation reached its expiry time. Check your current invitations.', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: fixture.organizationName, exact: true })).toHaveCount(0);
    if (fixture.boardName) await expect(page.getByRole('heading', { name: fixture.boardName, exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Accept reviewed invitation' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Retry invitation acceptance' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Review invitation', exact: true })).toHaveCount(0);
    expect(writes).toBe(0);
    const after = await page.request.get('/me/invitations'); expect(after.status()).toBe(200); expect(await after.json()).toEqual(pending);
    expect(await page.evaluate(() => sessionStorage.length + localStorage.length)).toBe(0);
    await page.clock.setSystemTime(new Date(originalTime)); await page.clock.resume();
    await page.goto(`/invitation#token=${fixture.token}`); await expect(page).toHaveURL(/\/invitation$/);
    await page.getByRole('button', { name: 'Review invitation', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Accept reviewed invitation' })).toBeVisible(); expect(writes).toBe(0);
  } finally { page.off('request', observe); }
}
