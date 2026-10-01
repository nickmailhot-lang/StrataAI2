import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-02/03/18: keyboard deactivation confirms and retries a lost acknowledgment at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `deactivation-ack-${Date.now()}@example.test`, password: 'browser-deactivate-correct-horse', displayName: 'Deactivation retry account' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const originalCookies = await context.cookies();
    const keys: string[] = [];
    await page.route('**/me/deactivate', async route => {
      keys.push(route.request().headers()['idempotency-key']);
      if (keys.length === 1) {
        expect((await route.fetch()).status()).toBe(204);
        // Losing every response header leaves the original opaque cookie intact.
        await context.addCookies(originalCookies);
        await route.abort('timedout');
      } else await route.continue();
    });
    await page.goto('/app/demo/profile');
    const deactivate = page.getByRole('button', { name: 'Deactivate account', exact: true });
    await deactivate.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('dialog', { name: 'Deactivate your account?' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Keep account active' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0); expect(keys).toHaveLength(0);
    await deactivate.focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Confirm deactivation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Retry deactivation' })).toBeVisible();
    await expect(page.getByText(credentials.email, { exact: true })).toHaveCount(0);
    expect((await context.request.get('/me')).status()).toBe(401);
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await page.getByRole('button', { name: 'Retry deactivation' }).focus(); await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByText('Your account is deactivated. Historical activity is preserved.')).toBeVisible();
    expect(keys).toHaveLength(2); expect(keys[1]).toBe(keys[0]); expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/i);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(401);
  });
}

test('PRD-02/03/18: mobile deactivation keeps a sole owner signed in with an actionable refusal', async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const credentials = { email: `sole-owner-${Date.now()}@example.test`, password: 'browser-sole-owner-correct-horse', displayName: 'Sole owner account' };
  expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
  expect((await context.request.post('/organizations', { headers, data: { name: 'Retained owner organization' } })).status()).toBe(201);
  await page.goto('/app/demo/profile');
  await page.getByLabel(/^Display name/).fill('Preserved owner draft');
  await page.getByRole('button', { name: 'Deactivate account', exact: true }).focus(); await page.keyboard.press('Enter');
  await page.getByRole('button', { name: 'Confirm deactivation' }).focus(); await page.keyboard.press('Enter');
  await expect(page.getByText(/Another active owner must take responsibility/)).toBeVisible();
  await expect(page.getByLabel(/^Display name/)).toHaveValue('Preserved owner draft');
  expect((await context.request.get('/me')).status()).toBe(200);
});

test('PRD-02/60: mobile keyboard registration retries a lost creation acknowledgment', async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const credentials = { email: `registration-ack-${Date.now()}@example.test`, password: 'browser-register-correct-horse', displayName: 'Registration retry account' };
  const keys: string[] = []; const ids: string[] = [];
  await page.route('**/auth/register', async route => {
    keys.push(route.request().headers()['idempotency-key']);
    const result = await route.fetch(); expect(result.status()).toBe(201);
    ids.push((await result.json()).user.id);
    if (keys.length === 1) await route.abort('timedout');
    else await route.fulfill({ response: result });
  });
  await page.goto('/login');
  await page.getByRole('tab', { name: 'Register', exact: true }).click();
  await page.getByLabel(/^Display name/).fill(credentials.displayName);
  await page.getByLabel(/^Email/).fill(credentials.email);
  await page.getByLabel(/^Password/).fill(credentials.password);
  await page.getByRole('button', { name: 'Create account' }).focus(); await page.keyboard.press('Enter');
  await expect(page.getByText(/Registration could not be confirmed/)).toBeVisible();
  await expect(page.getByLabel(/^Password/)).toHaveValue(credentials.password);
  await page.getByRole('button', { name: 'Create account' }).focus(); await page.keyboard.press('Enter');
  await expect(page.getByText('Account created. Sign in to continue.')).toBeVisible();
  await expect(page.getByLabel(/^Password/)).toHaveValue('');
  expect(keys).toHaveLength(2); expect(keys[1]).toBe(keys[0]); expect(ids[1]).toBe(ids[0]);
  expect((await context.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' }, data: credentials })).status()).toBe(200);
  const snapshot = await (await context.request.get('/me/sync?after=0')).json();
  expect(snapshot.events.filter((event: { eventType: string }) => event.eventType === 'USER_REGISTERED')).toHaveLength(1);
});

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-02/60: lost sign-in acknowledgment retries the original session at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const credentials = { email: `login-ack-${Date.now()}@example.test`, password: 'browser-login-correct-horse', displayName: 'Sign-in retry account' };
    expect((await context.request.post('/auth/register', { headers: { 'X-StrataAI-Request': '1' }, data: credentials })).status()).toBe(201);
    const keys: string[] = []; const cookies: string[] = [];
    await page.route('**/auth/login', async route => {
      keys.push(route.request().headers()['idempotency-key']);
      const result = await route.fetch();
      expect(result.status()).toBe(200);
      cookies.push(result.headers()['set-cookie'].split(';')[0]);
      if (keys.length === 1) { await context.clearCookies(); await route.abort('timedout'); }
      else await route.fulfill({ response: result });
    });
    await page.goto('/login');
    await page.getByLabel(/^Email/).fill(credentials.email);
    await page.getByLabel(/^Password/).fill(credentials.password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByText(/Retry with the same details/)).toBeVisible();
    expect((await context.request.get('/me')).status()).toBe(401);
    await page.getByRole('button', { name: 'Sign in', exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/app$/);
    expect(keys).toHaveLength(2); expect(keys[1]).toBe(keys[0]);
    expect(cookies[1]).toBe(cookies[0]);
    const profile = await context.request.get('/me'); expect(profile.status()).toBe(200);
    expect((await profile.json()).email).toBe(credentials.email);
  });
}

test('PRD-60-TC-07/11/15: verified email discovers an invitation and retries lost acceptance', async ({ page, context, browser }) => {
  const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  try {
    const headers = { 'X-StrataAI-Request': '1' };
    const owner = { email: `invite-owner-${Date.now()}@example.test`, password: 'browser-invite-correct-horse', displayName: 'Issuer' };
    const recipient = { ...owner, email: `invite-recipient-${Date.now()}@example.test`, displayName: 'Recipient' };
    expect((await issuer.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
    expect((await issuer.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
    expect((await context.request.post('/auth/register', { headers, data: recipient })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: recipient })).status()).toBe(200);
    const created = await issuer.request.post('/organizations', { headers, data: { name: 'Browser invitation council' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    expect((await issuer.request.post(`/organizations/${org}/invitations`, { headers, data: { email: recipient.email, surface: 'INTERNAL', targetRole: 'MEMBER' } })).status()).toBe(201);
    let attempts = 0; const paths: string[] = [];
    await page.route('**/me/invitations/*/accept', async route => {
      paths.push(new URL(route.request().url()).pathname);
      if (++attempts === 1) { expect((await route.fetch()).status()).toBe(200); await route.abort('timedout'); }
      else await route.continue();
    });
    await page.goto('/app');
    await page.getByRole('link', { name: 'Invitations', exact: true }).click();
    const accept = page.getByRole('button', { name: 'Accept invitation to Browser invitation council' });
    await expect(accept).toBeVisible(); await accept.focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Unable to confirm acceptance. You can retry this invitation safely.')).toBeVisible();
    const refresh = page.waitForResponse(response => new URL(response.url()).pathname === '/me/invitations' && response.request().method() === 'GET');
    await page.getByRole('button', { name: 'Refresh invitations' }).focus(); await page.keyboard.press('Enter');
    expect((await (await refresh).json()).items).toHaveLength(0);
    await expect(page.getByRole('heading', { name: 'Browser invitation council', exact: true })).toHaveCount(0);
    await expect(accept).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Retry invitation acceptance' })).toBeEnabled();
    await page.getByRole('button', { name: 'Retry invitation acceptance' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('link', { name: 'Open organization', exact: true })).toBeVisible();
    expect(paths).toHaveLength(2); expect(paths[1]).toBe(paths[0]);
    await page.getByRole('link', { name: 'Open organization', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Browser invitation council', exact: true })).toBeVisible();
    const organizations = await (await context.request.get('/organizations')).json();
    expect(organizations.filter((item: { organization: { id: string } }) => item.organization.id === org)).toHaveLength(1);
  } finally { await issuer.close(); }
});

test('PRD-02/60-TC-06: lost logout acknowledgment retries the original session receipt', async ({ page, context }) => {
  const headers = { 'X-StrataAI-Request': '1' };
  const credentials = { email: `logout-ack-${Date.now()}@example.test`, password: 'browser-logout-correct-horse', displayName: 'Logout retry account' };
  expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
  const originalCookies = await context.cookies();
  let suspendRecovery = false;
  await page.route('**/me/sync**', route => suspendRecovery ? route.abort() : route.continue());
  const keys: string[] = [];
  await page.route('**/auth/logout', async route => {
    keys.push(route.request().headers()['idempotency-key']);
    if (keys.length === 1) {
      const result = await route.fetch();
      expect(result.status()).toBe(204);
      // route.fetch applies cookies itself; restore the cookie to simulate losing all response headers.
      await context.addCookies(originalCookies);
      await route.abort('timedout');
    } else await route.continue();
  });
  await page.goto('/app/demo/profile');
  await expect(page.getByLabel(/^Display name/)).toHaveValue('Logout retry account');
  suspendRecovery = true;
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByText('Unable to sign out. Please retry.')).toBeVisible();
  expect((await context.request.get('/me')).status()).toBe(401);
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  expect(keys).toHaveLength(2);
  expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/i);
  expect(keys[1]).toBe(keys[0]);
  expect((await context.cookies()).some(cookie => cookie.name === 'strataai_session')).toBe(false);
});

test('PRD-02/60-TC-06/07: lost acknowledgment retries the committed profile intent once', async ({ page, context }) => {
  const headers = { 'X-StrataAI-Request': '1' };
  const credentials = { email: `lost-ack-${Date.now()}@example.test`, password: 'browser-retry-correct-horse', displayName: 'Retry account' };
  expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
  const initial = await (await context.request.get('/me/sync')).json();
  let suspendRecovery = false;
  // Simulate a disconnect that also prevents authoritative recovery until the explicit retry.
  await page.route('**/me/sync**', route => suspendRecovery ? route.abort() : route.continue());
  const keys: string[] = [];
  const bodies: string[] = [];
  let committed: { id: string; version: number; displayName: string } | undefined;
  await page.route('**/me', async route => {
    if (route.request().method() !== 'PATCH') { await route.continue(); return; }
    keys.push(route.request().headers()['idempotency-key']);
    bodies.push(route.request().postData()!);
    if (keys.length === 1) {
      const result = await route.fetch();
      expect(result.status()).toBe(200);
      committed = await result.json();
      await route.abort('timedout');
    } else await route.continue();
  });
  await page.goto('/app/demo/profile');
  await expect(page.getByLabel(/^Display name/)).toHaveValue('Retry account');
  suspendRecovery = true;
  await page.getByLabel(/^Display name/).fill('Saved after lost acknowledgment');
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect(page.getByText(/Unable to confirm your profile save/)).toBeVisible();
  await expect(page.getByLabel(/^Display name/)).toHaveValue('Saved after lost acknowledgment');
  await page.getByRole('button', { name: 'Save profile', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Profile saved.' })).toHaveText('Profile saved.');
  expect(keys).toHaveLength(2);
  expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/i);
  expect(keys[1]).toBe(keys[0]);
  expect(bodies[1]).toBe(bodies[0]);
  expect(committed?.version).toBe(initial.profile.version + 1);
  const replay = await (await context.request.get(`/me/sync?after=${initial.cursor}`)).json();
  expect(replay.profile).toMatchObject(committed!);
  expect(replay.events).toHaveLength(1);
  expect(replay.events[0].eventType).toBe('USER_PROFILE_UPDATED');
  expect(replay.cursor).toBe(initial.cursor + 1);
});

test('PRD-02-TC-06/11/12: mobile keyboard recovery preserves email after an invalid acknowledgment', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const email = `unknown-recovery-ack-${Date.now()}@example.test`;
  const attempts: string[] = [];
  const keys: string[] = [];
  await page.route('**/auth/password/forgot', async route => {
    attempts.push(route.request().postData()!);
    keys.push(route.request().headers()['idempotency-key']);
    const response = await route.fetch();
    expect(response.status()).toBe(202);
    if (attempts.length === 1) await route.fulfill({ response, body: '{}' });
    else await route.fulfill({ response });
  });
  await page.goto('/forgot-password');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Email/).press('Enter');
  await expect(page.getByRole('alert')).toContainText('could not be confirmed');
  await expect(page.getByRole('status')).toHaveCount(0);
  await expect(page.getByLabel(/^Email/)).toHaveValue(email);
  await page.getByLabel(/^Email/).press('Enter');
  await expect(page.getByRole('status')).toContainText('Request received.');
  expect(attempts).toHaveLength(2);
  expect(attempts[1]).toBe(attempts[0]);
  expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
  expect(keys[1]).toBe(keys[0]);
});

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
