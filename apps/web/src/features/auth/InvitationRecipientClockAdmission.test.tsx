import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { InvitationsPage } from './InvitationsPage';
import { InvitationLinkPage } from './InvitationLinkPage';

vi.mock('./invitationRecipientLive', () => ({ watchInvitationRecipient: vi.fn((options: { invalidate(reason: string): void }) => { options.invalidate('reset'); return vi.fn(); }) }));
const actor = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const token = 'private_calendar_invitation_proof_12345678901234567890';
const base = { id: '11111111-1111-4111-8111-111111111111', organizationId: '22222222-2222-4222-8222-222222222222',
  organizationName: 'Private recipient council', surface: 'INTERNAL', targetRole: 'MEMBER', expiresAt: '2035-01-08T18:00:00Z' };
const variants = [
  { surface: 'INTERNAL', bad: '2035-02-30T12:00:00Z', extra: {} },
  { surface: 'PORTAL', bad: '2035-02-29T12:00:00+05:30', extra: { surface: 'PORTAL', targetRole: 'OWNER' } },
  { surface: 'BOARD', bad: '2035-03-08T24:00:00Z', extra: { boardTarget: { boardId: '33333333-3333-4333-8333-333333333333', role: 'MEMBER' }, boardName: 'Private recipient Board' } },
];
const reply = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
afterEach(() => vi.unstubAllGlobals());
function mount(link: boolean) {
  return render(<RouterProvider router={createMemoryRouter([
    { path: '/app/invitations', element: <InvitationsPage /> }, { path: '/invitation', element: <InvitationLinkPage /> },
  ], { initialEntries: [link ? `/invitation#token=${token}` : '/app/invitations'] })} />);
}
for (const link of [false, true]) {
  function setup(item: typeof base & Record<string, unknown>, bad: string) {
    let malformed = true; const proofs: string[] = []; let acceptances = 0;
    vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') return reply({ id: actor });
      if (path.startsWith('/me/invitations/') && options?.method === 'POST') { acceptances++; throw new Error('Unexpected acceptance'); }
      const body = { ...item, expiresAt: malformed ? bad : item.expiresAt };
      if (path.startsWith('/invitations/review?')) { proofs.push(options!.body as string); return reply(body); }
      if (path.startsWith('/me/invitations?')) return reply({ items: [body], nextCursor: null });
      throw new Error('Unexpected recipient request');
    })); mount(link);
    return { recover: () => { malformed = false; }, proofs, acceptances: () => acceptances };
  }
  it.each(variants)('withholds $surface metadata and consent for an impossible expiry, then rechecks actual data (link=' + link + ')', async variant => {
    const state = setup({ ...base, ...variant.extra }, variant.bad);
    if (link) fireEvent.click(await screen.findByRole('button', { name: 'Review invitation' }));
    await screen.findByText(link ? 'The invitation could not be reviewed. Wait and retry.' : 'Unable to load invitations. Please refresh and try again.');
    expect(screen.queryByRole('heading', { name: base.organizationName })).not.toBeInTheDocument();
    expect(screen.queryByText('Private recipient Board')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Accept/ })).not.toBeInTheDocument(); expect(state.acceptances()).toBe(0);
    const retry = screen.getByRole('button', { name: link ? 'Review invitation' : 'Refresh invitations' });
    await waitFor(() => expect(retry).not.toHaveAttribute('aria-disabled', 'true')); await waitFor(() => expect(retry).toBeEnabled());
    state.recover(); fireEvent.click(retry); await screen.findByRole('heading', { name: base.organizationName });
    expect(screen.getByRole('button', { name: /^Accept/ })).toBeEnabled(); expect(state.acceptances()).toBe(0);
    if (link) { expect(state.proofs).toHaveLength(2); expect(state.proofs[1]).toBe(state.proofs[0]); expect(JSON.parse(state.proofs[0])).toEqual({ token }); }
  });
  it.each(variants)('preserves valid $surface leap-day offset expiry and seven fractional digits (link=' + link + ')', async variant => {
    const state = setup({ ...base, ...variant.extra, expiresAt: '2036-02-29T00:30:00.1234567+14:00' }, '2036-02-29T00:30:00.1234567+14:00');
    if (link) fireEvent.click(await screen.findByRole('button', { name: 'Review invitation' }));
    await screen.findByRole('heading', { name: base.organizationName });
    expect(screen.getByRole('button', { name: /^Accept/ })).toBeEnabled(); expect(state.acceptances()).toBe(0);
  });
}
it('does not interpret an offset-free recipient expiry in the browser timezone', async () => {
  vi.stubGlobal('fetch', vi.fn(async (path: string) => reply(path === '/me' ? { id: actor } : { items: [{ ...base, expiresAt: '2035-01-08T18:00:00' }], nextCursor: null })));
  mount(false); await screen.findByText('Unable to load invitations. Please refresh and try again.');
  expect(screen.queryByRole('heading', { name: base.organizationName })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /^Accept/ })).not.toBeInTheDocument();
});
