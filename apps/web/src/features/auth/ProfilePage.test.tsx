import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProfilePage } from './ProfilePage';

const profile = { id: 'user-1', email: 'council@example.test', displayName: 'Council', avatarUrl: null, locale: 'en-CA', timezone: 'America/Vancouver', status: 'active', emailVerified: true, version: 1 };
function renderProfile() {
  render(<MemoryRouter initialEntries={['/profile']}><Routes><Route path="/profile" element={<ProfilePage />} /><Route path="/login" element={<p>Sign in again</p>} /></Routes></MemoryRouter>);
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

describe('PRD-02 profile management', () => {
  it('AC-AUTH-02-03 periodically recovers preferences without a focus event', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, timezone: 'UTC', version: 2 }))));
    renderProfile();
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('America/Vancouver');
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('UTC');
  });

  it('PRD-02-TC-08 a read started before save cannot overwrite its authoritative acknowledgment', async () => {
    let finish: ((response: Response) => void) | undefined;
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Saved name', version: 2 }))));
    renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.focus(window);
    fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Saved name' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Profile saved.');
    await act(async () => { finish?.(new Response(JSON.stringify(profile))); });
    expect(screen.getByLabelText(/Display name/)).toHaveValue('Saved name');
    expect(screen.getByRole('heading', { name: 'Saved name' })).toBeInTheDocument();
  });

  it('AC-AUTH-02-03 recovers another client preferences on focus without a manual reload', async () => {
    const latest = { ...profile, timezone: 'UTC', version: 2 };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(JSON.stringify(latest))));
    renderProfile();
    await screen.findByLabelText(/Timezone/);
    fireEvent.focus(window);
    await waitFor(() => expect(screen.getByLabelText(/Timezone/)).toHaveValue('UTC'));
  });

  it('PRD-02-TC-08 automatic refresh preserves dirty edits and requires an explicit latest-profile reload', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, timezone: 'UTC', version: 2 }))));
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
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(null, { status: 401 })));
    renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.focus(window);
    await screen.findByText('Sign in again');
    expect(screen.queryByText(profile.email)).not.toBeInTheDocument();
  });

  it('PRD-02-TC-06 times out an abort-ignoring read and fences its late profile after recovery', async () => {
    let finish: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve; }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, timezone: 'UTC', version: 3 })));
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
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Your profile changed elsewhere.' }), { status: 409 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(latest)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...latest, displayName: 'Merged', version: 3 })));
    vi.stubGlobal('fetch', fetchMock);
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Display name/), { target: { value: 'My edits' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Your profile changed elsewhere.');
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
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Updated council', timezone: 'UTC' })));
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
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockRejectedValueOnce(new Error('Offline'))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...profile, displayName: 'Edited' })));
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
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'A valid timezone is required.' }), { status: 400 })));
    renderProfile();
    fireEvent.change(await screen.findByLabelText(/Timezone/), { target: { value: 'Invalid' } });
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('A valid timezone is required.');
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('Invalid');
    fireEvent.click(screen.getByRole('button', { name: 'Discard changes' }));
    expect(screen.getByLabelText(/Timezone/)).toHaveValue('America/Vancouver');
  });

  it('redirects when the session expires during a save', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(null, { status: 401 })));
    renderProfile();
    await screen.findByLabelText(/Display name/);
    fireEvent.submit(screen.getByRole('form', { name: 'Edit profile' }));
    await screen.findByText('Sign in again');
  });

  it('keeps the profile visible when sign out fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(profile)))
      .mockResolvedValueOnce(new Response(null, { status: 503 })));
    renderProfile();
    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }));
    await screen.findByText('Unable to sign out. Please retry.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign out' })).toBeEnabled());
  });
});
