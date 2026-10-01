import { expect, test } from '@playwright/test';

test('PRD-02-TC-03/04: recovery confirmation is generic and invalid reset links recover safely', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('link', { name: 'Forgot password?' }).click();
  await page.getByLabel(/^Email/).fill(`unknown-${Date.now()}@example.test`);
  await page.getByRole('button', { name: 'Request reset', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Request received.');
  await page.goto('/reset-password#token=invalid-browser-token');
  await expect(page).toHaveURL(/\/reset-password$/);
  await page.getByLabel(/^New password/).fill('new-correct-horse-battery');
  await page.getByLabel(/^Confirm new password/).fill('new-correct-horse-battery');
  await page.getByRole('button', { name: 'Reset password', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'The token is invalid or expired.' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reset password', exact: true })).toHaveCount(0);
  await page.getByRole('link', { name: 'Request a new link' }).click();
  await expect(page).toHaveURL(/\/forgot-password$/);
});

test('ARCH-11-TC-17 / PRD-02-TC-01/08: authenticated profile persistence and two-browser conflict recovery', async ({ page, context }) => {
  test.setTimeout(75_000);
  let liveSnapshots = 0;
  const liveEventTypes: string[] = [];
  page.on('websocket', socket => {
    if (!new URL(socket.url()).pathname.startsWith('/me/live')) return;
    socket.on('framereceived', frame => {
      for (const item of frame.payload.toString().split('\u001e').filter(Boolean)) {
        const message = JSON.parse(item);
        if (message.type === 2 && message.item?.profile) {
          liveSnapshots++;
          for (const event of message.item.events) liveEventTypes.push(event.eventType);
        }
      }
    });
  });
  const email = `browser-${Date.now()}@example.test`;
  const password = 'browser-correct-horse-battery';
  await page.goto('/login');
  await page.getByRole('tab', { name: 'Register', exact: true }).click();
  await page.getByLabel(/^Display name/).fill('Browser Council');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await page.getByLabel(/^Password/).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/app$/);
  await page.getByRole('link', { name: 'Open profile' }).click();
  await expect(page.getByLabel(/^Display name/)).toHaveValue('Browser Council');
  await expect.poll(() => liveSnapshots, { timeout: 10_000 }).toBeGreaterThan(0);
  const syncBefore = await (await context.request.get('/me/sync')).json();
  expect(syncBefore.events).toEqual([]);
  const second = await context.newPage();
  await second.setViewportSize({ width: 390, height: 844 });
  await second.goto('/app/demo/profile');
  await expect(second.getByLabel(/^Display name/)).toHaveValue('Browser Council');
  await second.getByLabel(/^Display name/).fill('Stale browser save');
  await page.getByLabel(/^Display name/).fill('First browser save');
  await page.getByLabel(/^Timezone/).fill('UTC');
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Profile saved.');
  await expect.poll(() => liveEventTypes.includes('USER_PROFILE_UPDATED'), { timeout: 10_000 }).toBe(true);
  const currentProfile = await (await context.request.get('/me')).json();
  const replay = await (await context.request.get(`/me/sync?after=${syncBefore.cursor}`)).json();
  expect(replay.events).toHaveLength(1);
  expect(replay.events[0]).toMatchObject({ eventType: 'USER_PROFILE_UPDATED', entityId: currentProfile.id,
    version: currentProfile.version, organizationId: null, boardId: null, metadata: {} });
  expect(replay.events[0].correlationId).toBeTruthy();
  await expect(page.locator(`time[datetime="${currentProfile.updatedAt}"]`)).toContainText(currentProfile.updatedAt.slice(11, 16));
  await expect(page.locator(`time[datetime="${currentProfile.updatedAt}"]`)).toContainText('UTC');
  await second.bringToFront();
  await expect(second.getByRole('alert')).toContainText('changed elsewhere', { timeout: 15_000 });
  await expect(second.getByLabel(/^Display name/)).toHaveValue('Stale browser save');
  await expect(second.getByRole('button', { name: 'Save profile', exact: true })).toBeDisabled();
  await second.getByRole('button', { name: 'Discard edits and load latest profile' }).click();
  await expect(second.getByLabel(/^Display name/)).toHaveValue('First browser save');
  await expect(second.getByLabel(/^Timezone/)).toHaveValue('UTC');
  await second.getByLabel(/^Display name/).fill('Merged browser save');
  await second.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect(second.getByRole('status')).toHaveText('Profile saved.');
  await page.bringToFront();
  await expect(page.getByLabel(/^Display name/)).toHaveValue('Merged browser save', { timeout: 15_000 });
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  await second.bringToFront();
  await expect(second).toHaveURL(/\/login$/, { timeout: 15_000 });
});
