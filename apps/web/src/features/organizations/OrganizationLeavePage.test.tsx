import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationLeavePage } from './OrganizationLeavePage';

const summary = { organization: { id: 'org', name: 'Private Organization', status: 0 }, role: 2 };
const reply = (value: unknown, status = 200) => status === 204 ? new Response(null, { status }) : new Response(JSON.stringify(value), { status });
function mount() {
  render(<RouterProvider router={createMemoryRouter([
    { path: '/app/:organizationId/leave', element: <OrganizationLeavePage /> },
    { path: '/login', element: <h1>Sign in destination</h1> },
  ], { initialEntries: ['/app/org/leave'] })} />);
}
async function confirm() {
  fireEvent.click(await screen.findByRole('button', { name: 'Review departure' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel departure' })).toHaveFocus());
  fireEvent.click(screen.getByRole('button', { name: 'Confirm departure' }));
}
afterEach(() => vi.unstubAllGlobals());
it('PRD-03-TC-01/11 requires confirmation and leaves only on an authoritative 204', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply(null, 204));
  vi.stubGlobal('fetch', fetcher); mount();
  fireEvent.click(await screen.findByRole('button', { name: 'Review departure' }));
  fireEvent.click(screen.getByRole('button', { name: 'Cancel departure' }));
  expect(fetcher).toHaveBeenCalledOnce();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await confirm(); await screen.findByText('You left the Organization.');
  expect(fetcher.mock.calls[1][0]).toBe('/organizations/org/leave');
  expect(fetcher.mock.calls[1][1].method).toBe('POST');
  expect(screen.queryByText('Private Organization')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole('status')).toHaveFocus());
});
it('PRD-03-TC-03 explains sole-owner refusal without offering another unreviewed departure', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ ...summary, role: 0 }))
    .mockResolvedValueOnce(reply({ code: 'sole_owner', detail: 'private failure' }, 409)));
  mount(); await confirm(); await screen.findByText('The last usable owner cannot leave. Another usable owner must remain.');
  expect(screen.queryByRole('button', { name: 'Review departure' })).not.toBeInTheDocument();
  expect(screen.queryByText('private failure')).not.toBeInTheDocument();
});
it('PRD-03-TC-06 does not infer success or repeat a lost departure before current membership review', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
    .mockResolvedValueOnce(reply({}, 404));
  vi.stubGlobal('fetch', fetcher); mount(); await confirm();
  await screen.findByText(/Your departure could not be confirmed/);
  expect(screen.queryByText('You left the Organization.')).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Review current membership' }));
  await screen.findByText('This Organization is unavailable to your account.');
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1);
});
it.each([401, 403, 404])('PRD-03-TC-05 clears private membership after departure access refusal (%s)', async status => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({}, status)));
  mount(); await confirm(); await screen.findByText(status === 401 ? 'Sign in destination' : 'This Organization is unavailable to your account.');
  expect(screen.queryByText('Private Organization')).not.toBeInTheDocument();
});
