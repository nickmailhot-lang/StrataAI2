import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProfilePage } from './ProfilePage';
vi.mock('./identityLive', () => ({ watchIdentity: () => () => {} }));

const profile = { id: 'user-1', email: 'council@example.test', displayName: 'Council', avatarUrl: null, locale: 'en-CA', timezone: 'America/Vancouver', status: 'active', emailVerified: true, version: 1, createdAt: '2026-03-08T09:30:00Z', updatedAt: '2026-03-08T10:30:00Z' };
function syncResponse(user: typeof profile, after?: number) {
  const events = after === undefined ? [] : Array.from({ length: user.version - after }, (_, index) => ({
    eventId: `00000000-0000-0000-0000-${String(after + index + 1).padStart(12, '0')}`,
    sequence: after + index + 1, eventType: 'USER_PROFILE_UPDATED', actorId: user.id,
    entityType: 'User', entityId: user.id, version: user.version, organizationId: null, boardId: null,
    metadata: {}, createdAt: user.updatedAt,
  }));
  return new Response(JSON.stringify({ profile: user, cursor: user.version, latestSequence: user.version, hasMore: false, events }));
}
function renderProfile() {
  return render(<MemoryRouter initialEntries={['/profile']}><Routes><Route path="/profile" element={<ProfilePage />} /><Route path="/login" element={<p>Sign in again</p>} /></Routes></MemoryRouter>);
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

describe('PRD-02 profile management', () => {
  it('PRD-02/60-TC-07 starts a new retry intent when preserved edits change after a network failure', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockRejectedValueOnce(new Error('Connection lost'))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Revised edit', version: 2 })));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    await waitFor(() => expect(screen.getByLabelText(/Display name/)).toHaveValue('Council'));
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Initial edit' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText(/Unable to confirm your profile save/);
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Revised edit' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Profile saved.');
    const firstKey = new Headers(fetchMock.mock.calls[1][1].headers).get('Idempotency-Key');
    const secondKey = new Headers(fetchMock.mock.calls[2][1].headers).get('Idempotency-Key');
    expect(firstKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(secondKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(secondKey).not.toBe(firstKey);
    for (const index of [1, 2]) expect(new Headers(fetchMock.mock.calls[index][1].headers).get('X-StrataAI-Expected-User')).toBe(profile.id);
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toMatchObject({ displayName: 'Initial edit', version: 1 });
    expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toMatchObject({ displayName: 'Revised edit', version: 1 });
  });

  it.each(['transport', 'body'])('PRD-02-TC-06 bounds a stalled save %s and ignores its late acknowledgment', async phase => {
    vi.useFakeTimers();
    let finish: ((value: unknown) => void) | undefined;
    const stalled = new Promise(resolve => { finish = resolve; });
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockImplementationOnce(() => phase === 'transport' ? stalled : Promise.resolve({ status: 200, ok: true, json: () => stalled }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Retry saved', version: 2 })));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    await act(async () => { await Promise.resolve(); });
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'My edits' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(fetchMock.mock.calls[1][1].signal.aborted).toBe(true);
    expect(screen.getByText(/Unable to confirm your profile save/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Display name/)).toHaveValue('My edits');
    expect(fetchMock).toHaveBeenCalledTimes(2);
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByText('Profile saved.')).toBeInTheDocument();
    const firstKey = new Headers(fetchMock.mock.calls[1][1].headers).get('Idempotency-Key');
    expect(firstKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(new Headers(fetchMock.mock.calls[2][1].headers).get('Idempotency-Key')).toBe(firstKey);
    const late = { ...profile, displayName: 'Late result', version: 9 };
    await act(async () => { finish?.(phase === 'transport' ? new Response(JSON.stringify(late)) : late); });
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Retry saved');
  });

  it.each([{ ...profile, id: 'another-user', version: 2 }, { ...profile, version: 1 }, { version: 2 }])('PRD-02-TC-03 rejects an invalid or unrelated save acknowledgment %#', async invalid => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(JSON.stringify(invalid))));
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'My edits' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText(/Unable to confirm your profile save/);
    expect(screen.getByLabelText(/Display name/)).toHaveValue('My edits');
    expect(screen.queryByText('Profile saved.')).not.toBeInTheDocument();
  });

  it('PRD-02-TC-06 bounds sign out and fences late success after retry', async () => {
    vi.useFakeTimers();
    let finish: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(new Response(null, { status: 503 }));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    await act(async () => { await Promise.resolve(); });
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(screen.getByText('Unable to sign out. Please retry.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    await act(async () => { await Promise.resolve(); });
    await act(async () => { finish?.(new Response(null, { status: 204 })); });
    const attempts = fetchMock.mock.calls.filter(call => call[0] === '/auth/logout');
    expect(attempts).toHaveLength(2);
    const firstKey = new Headers(attempts[0][1].headers).get('Idempotency-Key');
    expect(firstKey).toMatch(/^[0-9a-f-]{36}$/);
    expect(new Headers(attempts[1][1].headers).get('Idempotency-Key')).toBe(firstKey);
    expect(screen.queryByText('Sign in again')).not.toBeInTheDocument();
    expect(screen.getByText(profile.email)).toBeInTheDocument();
  });

  it('PRD-02-TC-06 aborts a pending mutation on unmount without navigating on late completion', async () => {
    let finish: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }));
    vi.stubGlobal('fetch', fetchMock);
    const view = renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    view.unmount();
    expect(fetchMock.mock.calls[1][1].signal.aborted).toBe(true);
    await act(async () => { finish?.(new Response(null, { status: 401 })); });
    expect(screen.queryByText('Sign in again')).not.toBeInTheDocument();
  });
  it('AC-AUTH-02-03 periodically recovers preferences without a focus event', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(syncResponse({ ...profile, timezone: 'UTC', version: 2 }, 1)));
    renderProfile();
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('America/Vancouver');
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('UTC');
    expect(screen.getByText(/Account created:/)).toHaveTextContent('09:30');
    expect(screen.getByText(/Last updated:/)).toHaveTextContent('10:30');
  });

  it('PRD-02-TC-08 a read started before save cannot overwrite its authoritative acknowledgment', async () => {
    let finish: ((response: Response) => void) | undefined;
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Saved name', version: 2 }))));
    renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.focus(window);
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Saved name' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Profile saved.');
    await act(async () => { finish?.(syncResponse(profile)); });
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Saved name');
    expect(screen.getByRole('heading', { name: 'Saved name' })).toBeInTheDocument();
  });

  it('AC-AUTH-02-03 recovers another client preferences on focus without a manual reload', async () => {
    const latest = { ...profile, timezone: 'UTC', version: 2 };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(syncResponse(latest, 1)));
    renderProfile();
    await screen.findByLabelText(/Timezone/);
    fireEvent.focus(window);
    await waitFor(() => expect(screen.getByLabelText(/Timezone/)).toHaveValue('UTC'));
  });

  it('PRD-02-TC-08 automatic refresh preserves dirty edits and requires an explicit latest-profile reload', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(syncResponse({ ...profile, timezone: 'UTC', version: 2 }, 1)));
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'My unsaved name' } });
    fireEvent.focus(window);
    await screen.findByText(/Your edits are preserved; load the latest profile/);
    expect(screen.getByLabelText(/Display name/)).toHaveValue('My unsaved name');
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('America/Vancouver');
    expect(screen.getByRole('button', { name: 'Save profile' })).toBeDisabled();
    expect(screen.getByText('en-CA · UTC')).toBeInTheDocument();
  });

  it('PRD-02-TC-05 clears the profile on a background session denial', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(null, { status: 401 })));
    renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.focus(window);
    await screen.findByText('Sign in again');
    expect(screen.queryByText(profile.email)).not.toBeInTheDocument();
  });

  it('PRD-02-TC-06 times out an abort-ignoring read and fences its late profile after recovery', async () => {
    let finish: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(syncResponse({ ...profile, timezone: 'UTC', version: 3 }, 1));
    vi.stubGlobal('fetch', fetchMock);
    vi.useFakeTimers();
    renderProfile();
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByLabelText(/Display name/)).toBeInTheDocument();
    fireEvent.focus(window);
    fireEvent.focus(window);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    expect(fetchMock.mock.calls[1][1].signal.aborted).toBe(true);
    expect(screen.getByText(/Unable to refresh your profile/)).toBeInTheDocument();
    fireEvent.focus(window);
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('UTC');
    await act(async () => { finish?.(new Response(JSON.stringify({ ...profile, timezone: 'Europe/Paris', version: 2 }))); });
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('UTC');
  });

  it('PRD-02-TC-08 preserves conflicted edits until explicit reload and saves with the latest version', async () => {
    const latest = { ...profile, displayName: 'Other browser', timezone: 'UTC', version: 2 };
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'version_conflict', title: 'private-server-conflict-details' }), { status: 409 }))
      .mockResolvedValueOnce(syncResponse(latest))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...latest, displayName: 'Merged', version: 3 })));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'My edits' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Your profile changed elsewhere.');
    expect(screen.queryByText('private-server-conflict-details')).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Display name/)).toHaveValue('My edits');
    expect(screen.getByRole('button', { name: 'Save profile' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits and load latest profile' }));
    await screen.findByRole('heading', { name: 'Other browser' });
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Merged' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Profile saved.');
    expect(JSON.parse(fetchMock.mock.calls[3][1].body).version).toBe(2);
  });
  it('saves editable preferences and displays the authoritative response', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Updated council', timezone: 'UTC', version: 2 })));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: ' Updated council ' } });
    fireEvent.change(screen.getByLabelText(/Timezone/), { target: { value: 'UTC' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Profile saved.');
    expect(screen.getByRole('heading', { name: 'Updated council' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Updated council');
    expect(fetchMock.mock.calls[1][0]).toBe('/me');
    expect(fetchMock.mock.calls[1][1]).toMatchObject({ method: 'PATCH', credentials: 'include' });
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toMatchObject({ timezone: 'UTC', avatarUrl: '', version: 1 });
  });

  it('preserves changes after a network error and allows retry', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockRejectedValueOnce(new Error('Offline'))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Edited', version: 2 })));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'Edited' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText(/Your changes are preserved/);
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Edited');
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Profile saved.');
  });

  it('shows server validation and discards edits only when requested', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'invalid_timezone', title: 'private-server-validation-details' }), { status: 400 })));
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Timezone/), { target: { value: 'Invalid' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('A valid timezone is required.');
    expect(screen.queryByText('private-server-validation-details')).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('Invalid');
    fireEvent.click(screen.getByRole('button', { name: 'Discard changes' }));
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('America/Vancouver');
  });

  it('redirects when the session expires during a save', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(null, { status: 401 })));
    renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Sign in again');
  });

  it('keeps the profile visible when sign out fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile))
      .mockResolvedValueOnce(new Response(null, { status: 503 })));
    renderProfile();
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }));
    await screen.findByText('Unable to sign out. Please retry.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign out' })).toBeEnabled());
  });
  it.each([204, 200])('clears retained invitation commands only after a confirmed sign-out (%s)', async status => {
    const storageKey = 'strataai:invitation-create:v1:actor:organization';
    sessionStorage.setItem(storageKey, 'private pending input'); sessionStorage.setItem('unrelated-site-data', 'keep');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile)).mockResolvedValueOnce(new Response(null, { status })));
    renderProfile(); fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }));
    await screen.findByText(status === 204 ? 'Sign in again' : 'Unable to sign out. Please retry.');
    expect(sessionStorage.getItem(storageKey)).toBe(status === 204 ? null : 'private pending input');
    expect(sessionStorage.getItem('unrelated-site-data')).toBe('keep');
    sessionStorage.removeItem(storageKey); sessionStorage.removeItem('unrelated-site-data');
  });
});


it('PRD-02: exposes a safe initial profile-read reference without provider text', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"title":"private-diagnostic"}', { status: 503, headers: { 'X-Correlation-ID': 'profile.read-1' } })));
  renderProfile(); await screen.findByText('Reference: profile.read-1');
  expect(screen.getByText('Unable to load your profile.')).toBeVisible();
  expect(screen.queryByText(/private-diagnostic/)).not.toBeInTheDocument();
});
it('PRD-02: pairs a failed background refresh with its own reference and retains edits', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile)).mockResolvedValueOnce(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'profile.refresh-1' } })));
  renderProfile(); fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'Unsaved' } });
  fireEvent(window, new Event('focus'));
  await screen.findByText('Reference: profile.refresh-1');
  expect(screen.getByRole('status')).toHaveTextContent('Unable to refresh your profile.');
  expect(screen.getByLabelText(/Display name/)).toHaveValue('Unsaved');
});
it.each(['save', 'logout'])('PRD-02: retains the actual %s refusal reference', async action => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile)).mockResolvedValueOnce(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'profile.command-1' } })));
  renderProfile(); await screen.findByLabelText(/Display name/);
  if (action === 'save') fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
  else fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
  await screen.findByText('Reference: profile.command-1');
  expect(screen.getByLabelText(/Display name/)).toHaveValue('Council');
});


it('PRD-02: retains the actual malformed acknowledgment reference without accepting a save', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile)).mockResolvedValueOnce(new Response('{}', { status: 200, headers: { 'X-Correlation-ID': 'profile.unconfirmed-1' } })));
  renderProfile(); fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'Unsaved' } });
  fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
  await screen.findByText('Reference: profile.unconfirmed-1');
  expect(screen.getByText(/Unable to confirm your profile save/)).toBeVisible();
  expect(screen.getByLabelText(/Display name/)).toHaveValue('Unsaved');
  expect(screen.queryByText('Profile saved.')).not.toBeInTheDocument();
});
it('PRD-02: withdraws the current profile and its refused response reference on authorization loss', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile)).mockResolvedValueOnce(new Response('{}', { status: 401, headers: { 'X-Correlation-ID': 'retired.profile-reference' } })));
  renderProfile(); await screen.findByLabelText(/Display name/);
  fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
  await screen.findByText('Sign in again');
  expect(screen.queryByText(/retired.profile-reference/)).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/Display name/)).not.toBeInTheDocument();
});
it('PRD-02: clears the old profile command reference during retry and invents none on network failure', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(syncResponse(profile))
    .mockResolvedValueOnce(new Response('{}', { status: 503, headers: { 'X-Correlation-ID': 'previous.profile-1' } }))
    .mockRejectedValueOnce(new Error('private-network-detail'));
  vi.stubGlobal('fetch', fetchMock); renderProfile(); await screen.findByLabelText(/Display name/);
  fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
  await screen.findByText('Reference: previous.profile-1');
  fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
  expect(screen.queryByText(/previous.profile-1/)).not.toBeInTheDocument();
  await screen.findByText(/Unable to confirm your profile save/);
  expect(screen.queryByText(/Reference:|private-network-detail/)).not.toBeInTheDocument();
  expect(new Headers(fetchMock.mock.calls[2][1].headers).get('Idempotency-Key')).toBe(new Headers(fetchMock.mock.calls[1][1].headers).get('Idempotency-Key'));
});
it('PRD-02: rejects malformed profile response references while retaining fixed public wording', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(syncResponse(profile)).mockResolvedValueOnce(new Response('{"title":"private-body-detail"}', { status: 503, headers: { 'X-Correlation-ID': 'private:diagnostic' } })));
  renderProfile(); await screen.findByLabelText(/Display name/);
  fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
  await screen.findByText('Unable to save your profile. Please retry.');
  expect(screen.queryByText(/Reference:|private:diagnostic|private-body-detail/)).not.toBeInTheDocument();
});
