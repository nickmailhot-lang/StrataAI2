import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProfilePage } from './ProfilePage';

const profile = { id: 'user-1', email: 'council@example.test', displayName: 'Council', avatarUrl: null, locale: 'en-CA', timezone: 'America/Vancouver', status: 'active', emailVerified: true };
function renderProfile() {
  render(<MemoryRouter initialEntries={['/profile']}><Routes><Route path="/profile" element={<ProfilePage />} /><Route path="/login" element={<p>Sign in again</p>} /></Routes></MemoryRouter>);
}
afterEach(() => vi.unstubAllGlobals());

describe('PRD-02 profile management', () => {
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
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toMatchObject({ timezone: 'UTC', avatarUrl: '' });
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
