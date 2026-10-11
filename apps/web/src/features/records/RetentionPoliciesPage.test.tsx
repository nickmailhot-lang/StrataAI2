import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ThemeProvider } from '@mui/material';
import { appTheme } from '../../theme/appTheme';
import { RetentionPoliciesPage } from './RetentionPoliciesPage';

const watcher = vi.hoisted(() => ({ watch: vi.fn<(options: { invalidate: () => void }) => () => void>(() => vi.fn()) }));
vi.mock('../organizations/organizationMetadataLive', () => ({ watchOrganizationMetadata: watcher.watch }));
const org = '11111111-1111-4111-8111-111111111111'; const actor = '22222222-2222-4222-8222-222222222222'; const policy = '33333333-3333-4333-8333-333333333333';
const record = { id: policy, organizationId: org, version: 1, recordClass: 'document', category: 'contracts', jurisdiction: null,
  anchor: 'closed', retentionDays: 365, action: 'preserve', source: 'Private recorded source', notes: null, createdAt: '2026-10-10T12:00:00Z', updatedAt: '2026-10-10T12:00:00Z' };
function fixture(empty = false) {
  const state = { denied: false, unavailable: false };
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const body = url === '/me' ? { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
      : state.denied || state.unavailable ? { code: 'organization_not_found', detail: 'Private diagnostic must not appear' }
        : url.includes('/history') ? { organizationId: org, policyId: policy, items: [{ policy: record, actorId: actor, actorLabel: 'Historical owner', membershipId: actor, membershipVersion: 1, actingCapacity: 0, eventId: policy }], nextBeforeVersion: null }
          : { organizationId: org, items: empty || url.includes('?after=') ? [] : [record], nextAfter: null };
    return new Response(JSON.stringify(body), { status: url === '/me' ? 200 : state.denied ? 404 : state.unavailable ? 503 : 200 });
  })); return state;
}
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/retention-policies', element: <RetentionPoliciesPage /> }], { initialEntries: [`/app/${org}/retention-policies`] });
  render(<ThemeProvider theme={appTheme}><RouterProvider router={router} /></ThemeProvider>);
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks(); sessionStorage.clear(); });
describe('PRD-70 MUI private policy browsing', () => {
  it('shows explicit rules and historical attribution and restores focus when history closes', async () => {
    fixture(); mount(); const trigger = await screen.findByRole('button', { name: 'View history for Document contracts, Organization-wide' });
    fireEvent.click(trigger); const heading = await screen.findByRole('heading', { name: 'Retained policy history' });
    await waitFor(() => expect(heading).toHaveFocus()); expect(screen.getByText(/Historical owner/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Close history' })); expect(trigger).toHaveFocus();
    expect(screen.getByText(/365 elapsed UTC days after closure/)).toBeInTheDocument();
  });
  it('withdraws all private values after authority loss and never renders private diagnostics', async () => {
    const state = fixture(); mount(); await screen.findByText('Source: Private recorded source');
    state.denied = true; fireEvent.click(screen.getByRole('button', { name: 'Refresh policies' }));
    await screen.findByText('Retention policies are no longer available with your current access.');
    expect(screen.queryByText('Source: Private recorded source')).not.toBeInTheDocument();
    expect(screen.queryByText(/Private diagnostic/)).not.toBeInTheDocument();
  });
  it('offers a safe retry after degraded reads and explains the authorized empty page', async () => {
    const state = fixture(true); state.unavailable = true; mount();
    const retry = await screen.findByRole('button', { name: 'Retry policy read' }); state.unavailable = false; fireEvent.click(retry);
    await screen.findByText(/No retention policies are recorded in this page/);
  });
  it('refreshes current state after an original acknowledgment and moves focus to the page heading', async () => {
    let current = record;
    vi.stubGlobal('fetch', vi.fn(async (url: string, options: RequestInit = {}) => {
      if (options.method === 'PATCH') {
        const body = JSON.parse(options.body as string) as { version: number; policy: object };
        const acknowledgment = { ...record, ...body.policy, version: body.version + 1 };
        current = { ...record, version: 3, source: 'A later authoritative source' };
        return new Response(JSON.stringify(acknowledgment));
      }
      return new Response(JSON.stringify(url === '/me'
        ? { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' }
        : { organizationId: org, items: url.includes('?after=') ? [] : [current], nextAfter: null }));
    }));
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Edit policy for Document contracts, Organization-wide' }));
    fireEvent.change(await screen.findByLabelText('Reviewed source'), { target: { value: 'My original reviewed change' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review policy change' }));
    fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Confirm policy change' }));
    await screen.findByText('Source: A later authoritative source');
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Retention policies' })).toHaveFocus());
    expect(screen.queryByText('Source: My original reviewed change')).not.toBeInTheDocument();
    expect(screen.getByText('contracts · Revision 3')).toBeVisible();
  });
  it('withdraws an open private draft when metadata invalidation confirms lost authority', async () => {
    const state = fixture(); mount(); fireEvent.click(await screen.findByRole('button', { name: 'Edit policy for Document contracts, Organization-wide' }));
    fireEvent.change(await screen.findByLabelText('Reviewed source'), { target: { value: 'Private unsent draft' } });
    state.denied = true;
    const subscription = watcher.watch.mock.calls.at(-1)?.[0] as unknown as { invalidate: () => void };
    act(() => subscription.invalidate());
    await screen.findByText('Retention policies are no longer available with your current access.');
    expect(screen.queryByText('Source: Private recorded source')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('Private unsent draft')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Resume policy draft' })).not.toBeInTheDocument();
  });
});
