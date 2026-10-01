import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationInvitationHistoryPage } from './OrganizationInvitationHistoryPage';

const org = '10000000-0000-0000-0000-000000000001';
const profile = { locale: 'en-CA', timezone: 'Pacific/Honolulu' };
const row = { id: '20000000-0000-0000-0000-000000000001', email: 'issued@example.test', surface: 'INTERNAL', targetRole: 'MEMBER',
  createdAt: '2034-01-01T00:00:00Z', expiresAt: '2035-01-08T18:00:00Z', acceptedAt: null as string | null, revokedAt: null as string | null, deliveryState: 'SENT' };
const reply = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status });
const page = (items = [row]) => ({ items, nextCursor: null });
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/invitations', element: <OrganizationInvitationHistoryPage /> }],
    { initialEntries: [`/app/${org}/invitations`] });
  return render(<RouterProvider router={router} />);
}
function fetcher(...responses: (Response | Error)[]) {
  const mock = vi.fn(); for (const response of responses) {
    if (response instanceof Error) mock.mockRejectedValueOnce(response); else mock.mockResolvedValueOnce(response);
  } vi.stubGlobal('fetch', mock); return mock;
}
async function review() { fireEvent.click(await screen.findByRole('button', { name: `Revoke invitation for ${row.email}` })); await screen.findByRole('dialog'); }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe('PRD-60 administrator invitation history and revocation', () => {
  it('displays lifecycle separately from sending status using account time preferences', async () => {
    fetcher(reply(profile), reply(page())); mount(); await screen.findByText(row.email);
    expect(screen.getByText('Email sent')).toBeInTheDocument(); expect(screen.getByText('Awaiting acceptance')).toBeInTheDocument();
    expect(screen.getByText(/Expires:.*08:00/)).toBeInTheDocument(); expect(screen.getByText(/does not prove inbox/)).toBeInTheDocument();
    expect(sessionStorage.length).toBe(0); expect(localStorage.length).toBe(0);
  });
  it('requires explicit confirmation and cancellation sends no mutation', async () => {
    const mock = fetcher(reply(profile), reply(page())); mount(); await review();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus());
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(0);
  });
  it('confirms only a successful revocation acknowledgment then reloads canonical state', async () => {
    const mock = fetcher(reply(profile), reply(page()), reply(null, 204), reply(profile), reply(page([{ ...row, revokedAt: '2034-01-02T00:00:00Z' }])));
    mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    await screen.findByText('Revoked'); expect(screen.getByText('Invitation revocation confirmed.')).toBeInTheDocument();
    const calls = mock.mock.calls.filter(call => call[1]?.method === 'DELETE'); expect(calls).toHaveLength(1);
    expect(calls[0][0]).toContain(`/organizations/${org}/invitations/${row.id}`);
    expect(calls[0][1].headers.get('X-StrataAI-Request')).toBe('1');
  });
  it('recovers a lost acknowledgment through read-only canonical review without another delete', async () => {
    const mock = fetcher(reply(profile), reply(page()), new Error('lost'), reply(profile), reply(page([{ ...row, revokedAt: '2034-01-02T00:00:00Z' }])));
    mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    await screen.findByText(/Revocation could not be confirmed/); expect(screen.queryByText('Invitation revocation confirmed.')).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: 'Check revocation' })); await screen.findByText('Invitation revocation confirmed.');
    expect(mock.mock.calls.filter(call => call[1]?.method === 'DELETE')).toHaveLength(1);
  });
  it('does not treat an ambiguous not-found response as confirmed revocation', async () => {
    fetcher(reply(profile), reply(page()), reply({ title: 'private diagnostics' }, 404), reply(profile), reply(page()));
    mount(); await review(); fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Check revocation' }));
    await screen.findByText(/Revocation was not confirmed/); expect(screen.queryByText('Invitation revocation confirmed.')).not.toBeInTheDocument();
    expect(screen.queryByText('private diagnostics')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: `Revoke invitation for ${row.email}` })).toBeEnabled();
  });
  it('clears protected recipient information when the mutation loses authentication', async () => {
    fetcher(reply(profile), reply(page()), reply({}, 401)); mount(); await review();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' })); await screen.findByText(/Sign in again/);
    expect(screen.queryByText(row.email)).not.toBeInTheDocument(); expect(screen.queryByText('Email sent')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create invitation' })).not.toBeInTheDocument();
  });
  it('rejects malformed server role/surface data before displaying private rows', async () => {
    fetcher(reply(profile), reply(page([{ ...row, targetRole: 'SUPERUSER' }]))); mount();
    await screen.findByText(/Unable to confirm invitation history/); expect(screen.queryByText(row.email)).not.toBeInTheDocument();
  });
  it('uses the authoritative cursor and allows navigation back to the first page', async () => {
    const rows = Array.from({ length: 50 }, (_, n) => ({ ...row, id: `20000000-0000-0000-0000-${(n + 1).toString().padStart(12, '0')}`, email: `issued-${n}@example.test` }));
    const last = rows.at(-1)!.id;
    const mock = fetcher(reply(profile), reply({ items: rows, nextCursor: last }), reply(profile), reply(page([{ ...row, id: '30000000-0000-0000-0000-000000000001' }])), reply(profile), reply({ items: rows, nextCursor: last }));
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Next invitations' })); await screen.findByText(row.email);
    expect(mock.mock.calls[3][0]).toContain(`?after=${last}`);
    fireEvent.click(screen.getByRole('button', { name: 'Previous invitations' })); await screen.findByText('issued-0@example.test');
    expect(mock.mock.calls[5][0]).not.toContain('?after=');
  });
});
