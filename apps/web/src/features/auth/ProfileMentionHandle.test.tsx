import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ProfilePage } from './ProfilePage';
vi.mock('./identityLive', () => ({ watchIdentity: () => () => {} }));
afterEach(() => vi.unstubAllGlobals());
it('blocks competing account commands during handle recovery and preserves unsaved profile edits after confirmation', async () => {
  const id = '11111111-1111-1111-1111-111111111111'; let version = 1; let writes = 0;
  const profile = { id, email: 'own@example.test', displayName: 'Council', avatarUrl: null, locale: 'en', timezone: 'UTC',
    status: 'ACTIVE', emailVerified: true, version: 1, createdAt: '2026-10-03T10:00:00Z', updatedAt: '2026-10-03T10:00:00Z' };
  const fetchMock = vi.fn(async (path: string, options?: RequestInit) => {
    const json = (body: unknown) => new Response(JSON.stringify(body));
    const current = { ...profile, version };
    if (path.startsWith('/me/sync')) return json({ profile: current, cursor: version, latestSequence: version, hasMore: false, events: [] });
    if (path === '/me') return json(current);
    if (options?.method !== 'PATCH') return json({ userId: id, handle: version === 1 ? 'alice' : 'bob', userVersion: version,
      handleVersion: version, createdAt: profile.createdAt, updatedAt: version === 1 ? profile.updatedAt : '2026-10-03T10:00:01Z' });
    version = 2;
    if (++writes === 1) throw new Error('Lost reply');
    return json({ userId: id, handle: 'bob', userVersion: 2, handleVersion: 2, changed: true });
  });
  vi.stubGlobal('fetch', fetchMock); render(<MemoryRouter><ProfilePage /></MemoryRouter>);
  await waitFor(() => expect(screen.getByLabelText(/Display name/)).toHaveValue('Council'));
  fireEvent.change(screen.getByLabelText(/Display name/), { target: { value: 'Preserved draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Change mention handle' }));
  await waitFor(() => expect(screen.getByLabelText('Mention handle')).toHaveValue('alice'));
  fireEvent.change(screen.getByLabelText('Mention handle'), { target: { value: 'bob' } });
  fireEvent.submit(screen.getByRole('form', { name: 'Change mention handle' }));
  await screen.findByText(/Unable to confirm your handle change/);
  for (const name of ['Sign out', 'Deactivate account', 'Save profile', 'Discard changes'])
    expect(screen.getByRole('button', { name, hidden: true })).toBeDisabled();
  fireEvent.submit(screen.getByRole('form', { name: 'Edit profile', hidden: true }));
  expect(fetchMock.mock.calls.filter(([, options]) => options?.method === 'PATCH')).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: 'Retry original handle change' }));
  await screen.findByText('Handle change confirmed.'); fireEvent.click(screen.getByRole('button', { name: 'Close' }));
  await screen.findByText(/Your profile changed elsewhere. Your edits are preserved/);
  expect(screen.getByLabelText(/Display name/)).toHaveValue('Preserved draft');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Save profile' })).toBeDisabled());
});
