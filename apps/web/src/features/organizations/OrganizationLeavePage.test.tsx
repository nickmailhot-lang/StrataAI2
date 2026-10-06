import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationLeavePage } from './OrganizationLeavePage';

const summary = { organization: { id: 'org', name: 'Private Organization', status: 0 }, role: 2 };
const reply = (value: unknown, status = 200) => status === 204 ? new Response(null, { status }) : new Response(JSON.stringify(value), { status });
const actorId = '00000000-0000-4000-8000-000000000008';
function useFetch(fetcher: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>) {
  vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => input === '/me'
    ? Promise.resolve(reply({ id: actorId })) : fetcher(input, init));
}
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
  useFetch(fetcher); mount();
  fireEvent.click(await screen.findByRole('button', { name: 'Review departure' }));
  fireEvent.click(screen.getByRole('button', { name: 'Cancel departure' }));
  expect(fetcher).toHaveBeenCalledOnce();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await confirm(); await screen.findByText('You left the Organization.');
  expect(fetcher.mock.calls[1][0]).toBe('/organizations/org/leave');
  expect(fetcher.mock.calls[1][1].method).toBe('POST');
  expect(JSON.parse(fetcher.mock.calls[1][1].body)).toEqual({ expectedActorId: actorId });
  expect(screen.queryByText('Private Organization')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole('status')).toHaveFocus());
});
it('PRD-03-TC-03 explains sole-owner refusal without offering another unreviewed departure', async () => {
  useFetch(vi.fn().mockResolvedValueOnce(reply({ ...summary, role: 0 }))
    .mockResolvedValueOnce(reply({ code: 'sole_owner', detail: 'private failure' }, 409)));
  mount(); await confirm(); await screen.findByText('The last usable owner cannot leave. Another usable owner must remain.');
  expect(screen.queryByRole('button', { name: 'Review departure' })).not.toBeInTheDocument();
  expect(screen.queryByText('private failure')).not.toBeInTheDocument();
});
it('PRD-03-TC-06 recovers the original departure without treating a later rejoin as another departure', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
    .mockResolvedValueOnce(reply(null, 204)).mockResolvedValueOnce(reply(summary));
  useFetch(fetcher); mount(); await confirm();
  await screen.findByText(/Your departure could not be confirmed/);
  expect(screen.queryByText('You left the Organization.')).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review current membership' })).toBeDisabled());
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original departure' }));
  await screen.findByText('Original departure acknowledged. Review current membership to check later access.');
  expect(fetcher.mock.calls[2][1].body).toBe(fetcher.mock.calls[1][1].body);
  expect(fetcher.mock.calls[2][1].headers.get('Idempotency-Key')).toBe(fetcher.mock.calls[1][1].headers.get('Idempotency-Key'));
  expect(fetcher.mock.calls[1][1].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  expect(screen.queryByText('You left the Organization.')).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Review current membership' }));
  await screen.findByRole('button', { name: 'Review departure' });
  expect(fetcher.mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(2);
});
it('preserves the original departure key across repeated uncertainty and retires it on expiry refusal', async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
    .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({ code: 'idempotency_expired' }, 409));
  useFetch(fetcher); mount(); await confirm();
  fireEvent.click(await screen.findByRole('button', { name: 'Retry original departure' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original departure' })).toBeEnabled());
  expect(screen.getByRole('button', { name: 'Review current membership' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Retry original departure' }));
  await screen.findByText('The departure was refused. Review current membership before considering another departure.');
  expect(screen.queryByRole('button', { name: 'Retry original departure' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Review current membership' })).toBeEnabled();
  expect(new Set(fetcher.mock.calls.slice(1).map(call => call[1].headers.get('Idempotency-Key'))).size).toBe(1);
});
it.each([401, 403, 404])('clears original departure recovery after access refusal (%s)', async status => {
  useFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockRejectedValueOnce(new Error('Lost'))
    .mockResolvedValueOnce(reply({}, status)));
  mount(); await confirm(); fireEvent.click(await screen.findByRole('button', { name: 'Retry original departure' }));
  await screen.findByText(status === 401 ? 'Sign in destination' : 'This Organization is unavailable to your account.');
  expect(screen.queryByRole('button', { name: 'Retry original departure' })).not.toBeInTheDocument();
  expect(screen.queryByText('Private Organization')).not.toBeInTheDocument();
});
it.each([401, 403, 404])('PRD-03-TC-05 clears private membership after departure access refusal (%s)', async status => {
  useFetch(vi.fn().mockResolvedValueOnce(reply(summary)).mockResolvedValueOnce(reply({}, status)));
  mount(); await confirm(); await screen.findByText(status === 401 ? 'Sign in destination' : 'This Organization is unavailable to your account.');
  expect(screen.queryByText('Private Organization')).not.toBeInTheDocument();
});
