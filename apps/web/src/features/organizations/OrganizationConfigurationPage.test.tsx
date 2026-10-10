import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationConfigurationPage } from './OrganizationConfigurationPage';
import { parseOrganizationConfiguration, type ConfigurationRevision } from './organizationConfiguration';

const live = vi.hoisted(() => ({ watch: vi.fn<(options: { invalidate(): void; reset(): void; unavailable(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const organizationId = '11111111-1111-4111-8111-111111111111';
const actorId = '22222222-2222-4222-8222-222222222222';
const boardId = '33333333-3333-4333-8333-333333333333';
const listId = '44444444-4444-4444-8444-444444444444';
const profile = { id: actorId, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const path = `/organizations/${organizationId}/configuration`;
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
function record(version: number, legalName = 'Recorded legal name'): ConfigurationRevision {
  return { organizationId, version, configuration: parseOrganizationConfiguration({ legalName, jurisdiction: 'BC', timezone: 'UTC' }),
    organizationName: 'Historical display name', organizationType: 'STRATA', organizationVersion: 1, actorId,
    eventId: `55555555-5555-4555-8555-${String(version).padStart(12, '0')}`, correlationId: 'fixed-reference',
    createdAt: '2026-10-10T13:00:00.0000001Z', updatedAt: '2026-10-10T13:00:00.0000002Z' };
}
function fixture(initial: ConfigurationRevision | null = record(1)) {
  const state = { current: initial, denied: false, writes: [] as RequestInit[], lost: false, conflicted: false, boardUnavailable: false, listPages: false };
  const receipts = new Map<string, ConfigurationRevision>();
  vi.stubGlobal('fetch', vi.fn(async (url: string, options: RequestInit = {}) => {
    if (url === '/me') return reply(profile);
    if (state.denied) return reply({ code: 'organization_not_found', detail: 'Private diagnostic' }, 404);
    if (url === path && options.method === 'PATCH') {
      state.writes.push(options);
      const key = new Headers(options.headers).get('Idempotency-Key')!;
      const receipt = receipts.get(key); if (receipt) return reply(receipt);
      if (state.conflicted) return reply({ code: 'version_conflict' }, 409);
      const input = JSON.parse(options.body as string) as { version: number; configuration: ConfigurationRevision['configuration'] };
      const result = { ...record(input.version + 1), configuration: input.configuration }; state.current = result; receipts.set(key, result);
      if (state.lost) { state.lost = false; throw new TypeError('Lost reply'); }
      return reply(result);
    }
    if (url === path) return reply({ organizationId, version: state.current?.version ?? 0, revision: state.current });
    if (url.startsWith(path + '/history')) return reply({ organizationId, items: state.current ? [state.current] : [], nextBeforeVersion: null });
    if (url === `/organizations/${organizationId}/boards/directory`) return reply({ organizationId, items: [{ id: boardId, name: 'Actual intake Board', version: 1 }], nextCursor: null });
    if (url === `${path}/intake-boards/${boardId}/lists` && state.boardUnavailable) return reply({}, 503);
    if (url.startsWith(`${path}/intake-boards/${boardId}/lists`) && state.listPages) {
      const later = url.includes('?afterRank=');
      return reply({ organizationId, board: { id: boardId, name: 'Actual intake Board', version: 1 },
        items: later ? [{ id: '66666666-6666-4666-8666-666666666666', name: 'Later intake List', version: 1, rank: String(51).padStart(30, '0') }]
          : Array.from({ length: 50 }, (_, index) => ({ id: index === 0 ? listId : `77777777-7777-4777-8777-${String(index).padStart(12, '0')}`,
            name: index === 0 ? 'Actual intake List' : `Paged List ${index}`, version: 1, rank: String(index + 1).padStart(30, '0') })),
        nextAfterRank: later ? null : String(50).padStart(30, '0') });
    }
    if (url === `${path}/intake-boards/${boardId}/lists`) return reply({ organizationId, board: { id: boardId, name: 'Actual intake Board', version: 1 },
      items: [{ id: listId, name: 'Actual intake List', version: 1, rank: '000000000000000000000000000001' }], nextAfterRank: null });
    throw new Error('Unexpected fixture route');
  }));
  return state;
}
function mount() {
  const router = createMemoryRouter([{ path: '/app/:organizationId/configuration', element: <OrganizationConfigurationPage /> },
    { path: '/login', element: <h1>Sign in</h1> }, { path: '/app/:organizationId', element: <h1>Organization destination</h1> }],
  { initialEntries: [`/app/${organizationId}/configuration`] });
  render(<RouterProvider router={router} />); return router;
}
const reviewButton = () => screen.getByRole('button', { name: 'Review configuration change' });
async function review() { fireEvent.click(reviewButton()); return screen.findByRole('dialog', { name: 'Review configuration change' }); }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); sessionStorage.clear(); live.watch.mockClear(); });
describe('PRD-27 configuration form, human review and recovery', () => {
  it('restores an unresolved original after remount without rebasing it to a newer current revision or automatically sending', async () => {
    const state = fixture(); mount();
    fireEvent.change(await screen.findByLabelText(/^Legal name/), { target: { value: 'Original reviewed proposal' } });
    state.lost = true;
    fireEvent.click(within(await review()).getByRole('button', { name: 'Approve configuration change' }));
    await screen.findByRole('button', { name: 'Retry original submission' });
    expect(state.writes).toHaveLength(1); const body = state.writes[0].body;
    const originalKey = new Headers(state.writes[0].headers).get('Idempotency-Key');
    cleanup(); state.current = record(3, 'Later authoritative name'); mount();
    await screen.findByText('Current revision: 3');
    expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Original reviewed proposal');
    expect(reviewButton()).toBeDisabled(); expect(state.writes).toHaveLength(1);
    const retry = await screen.findByRole('button', { name: 'Retry original submission' });
    await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
    await screen.findByText('Change acknowledged. Current configuration revision 3.');
    expect(state.writes).toHaveLength(2); expect(state.writes[1].body).toBe(body);
    expect(new Headers(state.writes[1].headers).get('Idempotency-Key')).toBe(originalKey);
    expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Later authoritative name'); expect(sessionStorage.length).toBe(0);
  });
  it('prevents the mutation when the browser cannot retain the original', async () => {
    const state = fixture(); mount(); await screen.findByLabelText(/^Legal name/);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {});
    fireEvent.click(within(await review()).getByRole('button', { name: 'Approve configuration change' }));
    await screen.findByText(/This browser could not retain the original configuration submission/);
    expect(state.writes).toHaveLength(0); expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Recorded legal name');
  });
  it('pages Lists without losing the verified selected destination or retaining every prior page', async () => {
    const state = fixture(); state.listPages = true; mount(); await screen.findByDisplayValue('Recorded legal name');
    fireEvent.mouseDown(screen.getByLabelText('Intake Board')); fireEvent.click(await screen.findByRole('option', { name: 'Actual intake Board' }));
    const more = await screen.findByRole('button', { name: 'More intake Lists' });
    await waitFor(() => expect(screen.getByLabelText('Intake List')).not.toHaveAttribute('aria-disabled', 'true'));
    fireEvent.mouseDown(screen.getByLabelText('Intake List')); fireEvent.click(await screen.findByRole('option', { name: 'Actual intake List' }));
    fireEvent.click(more); await waitFor(() => expect(screen.queryByRole('button', { name: 'More intake Lists' })).not.toBeInTheDocument());
    fireEvent.mouseDown(screen.getByLabelText('Intake List'));
    expect(await screen.findByRole('option', { name: 'Actual intake List' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Paged List 1' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('option', { name: 'Later intake List' }));
    const dialog = await review(); expect(within(dialog).getByText('Later intake List')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve configuration change' }));
    await screen.findByText(/Change acknowledged. Current configuration revision/);
    expect(JSON.parse(state.writes[0].body as string)).toMatchObject({ configuration: { intakeBoardId: boardId, intakeListId: '66666666-6666-4666-8666-666666666666' } });
  });
  it('offers every named field and an explicit empty-state action without fabricated legal values', async () => {
    fixture(null); mount(); expect(await screen.findByText(/No configuration has been recorded/)).toBeInTheDocument();
    for (const label of ['Legal name', 'Jurisdiction', 'Organization timezone (IANA)', 'Corporation or registration identifier', 'Civic address',
      'Management company', 'Insurance renewal date', 'Lot count', 'Fiscal year end month', 'Fiscal year end day', 'AGM cycle (months)', 'Depreciation report cycle (months)',
      'Intake Board', 'Intake List']) expect(screen.getByLabelText(new RegExp('^' + label.replace(/[()]/g, '\\$&')))).toBeInTheDocument();
    expect(screen.getByLabelText(/^Legal name/)).toHaveValue(''); expect(screen.getByLabelText(/^Organization timezone/)).toHaveValue('');
    fireEvent.click(reviewButton()); await screen.findByText('Enter legal name.'); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    for (const name of ['Add emergency contact', 'Add category', 'Add priority', 'Add jurisdiction policy']) expect(screen.getByRole('button', { name })).toBeEnabled();
  });
  it('requires explicit review, displays policy sources and all proposed values, then refreshes authoritative state', async () => {
    const state = fixture(); mount(); fireEvent.change(await screen.findByLabelText(/^Legal name/), { target: { value: 'Proposed legal name' } });
    fireEvent.change(screen.getByLabelText('Management company'), { target: { value: 'New manager' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add jurisdiction policy' }));
    for (const [label, value] of [['Policy 1 key', 'notice'], ['Policy 1 value', 'Reviewed value'], ['Policy 1 source', 'Reviewed source'], ['Policy 1 notes', 'Reviewed notes']])
      fireEvent.change(screen.getByLabelText(label), { target: { value } });
    const dialog = await review(); expect(state.writes).toHaveLength(0);
    expect(within(dialog).getByText('Proposed legal name')).toBeInTheDocument(); expect(within(dialog).getByText('Recorded legal name')).toBeInTheDocument();
    expect(within(dialog).getByText('Source: Reviewed source')).toBeInTheDocument(); expect(within(dialog).getByText('Notes: Reviewed notes')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Return to draft' })).toHaveFocus();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve configuration change' }));
    await screen.findByText('Change acknowledged. Current configuration revision 2.'); expect(state.writes).toHaveLength(1);
    expect(JSON.parse(state.writes[0].body as string)).toMatchObject({ version: 1, configuration: { legalName: 'Proposed legal name', managementCompanyName: 'New manager' } });
    await waitFor(() => expect(reviewButton()).toHaveFocus());
  });
  it('preserves an uncertain original submission and exact key while an unrelated current read occurs', async () => {
    const state = fixture(); state.lost = true; mount(); fireEvent.change(await screen.findByLabelText(/^Legal name/), { target: { value: 'Preserved proposal' } });
    fireEvent.click(within(await review()).getByRole('button', { name: 'Approve configuration change' }));
    const retry = await screen.findByRole('button', { name: 'Retry original submission' });
    expect(reviewButton()).toBeDisabled(); expect(screen.getByLabelText(/^Legal name/)).toBeDisabled();
    state.current = record(3, 'Later authoritative name'); fireEvent.click(screen.getByRole('button', { name: 'Load current configuration' }));
    await screen.findByText('Current revision: 3'); expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Preserved proposal');
    expect(screen.getByRole('button', { name: 'Retry original submission' })).toBeEnabled(); fireEvent.click(retry);
    await screen.findByText('Change acknowledged. Current configuration revision 3.');
    expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Later authoritative name');
    expect(state.writes).toHaveLength(2); expect(state.writes[1].body).toBe(state.writes[0].body);
    expect(new Headers(state.writes[1].headers).get('Idempotency-Key')).toBe(new Headers(state.writes[0].headers).get('Idempotency-Key'));
  });
  it('preserves a conflicted draft and requires fresh current-state review before another change', async () => {
    const state = fixture(); mount(); fireEvent.change(await screen.findByLabelText(/^Legal name/), { target: { value: 'Preserved proposal' } });
    state.conflicted = true; fireEvent.click(within(await review()).getByRole('button', { name: 'Approve configuration change' }));
    await screen.findByText(/Configuration changed elsewhere. Your draft is preserved/);
    expect(await screen.findByRole('button', { name: 'Review configuration change' })).toBeDisabled(); expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Preserved proposal');
    expect(screen.queryByRole('button', { name: 'Retry original submission' })).not.toBeInTheDocument();
    state.current = record(3, 'Changed elsewhere'); state.conflicted = false;
    fireEvent.click(screen.getByRole('button', { name: 'Load current configuration' })); await screen.findByText('Current revision: 3');
    const dialog = await review(); expect(within(dialog).getByText('Changed elsewhere')).toBeInTheDocument();
    expect(within(dialog).getByText('Preserved proposal')).toBeInTheDocument(); expect(within(dialog).getByText(/Approval replaces configuration at revision 3/)).toBeInTheDocument();
  });
  it('withdraws all private draft, history and source values on a live permission loss', async () => {
    const state = fixture(); mount(); await screen.findByLabelText(/^Legal name/);
    fireEvent.click(screen.getByRole('button', { name: 'View configuration history' })); await screen.findByRole('heading', { name: 'Revision 1' });
    state.denied = true; act(() => live.watch.mock.calls.at(-1)![0].invalidate());
    await screen.findByText('Organization configuration is unavailable to your account.');
    expect(screen.queryByLabelText(/^Legal name/)).not.toBeInTheDocument(); expect(screen.queryByText('Recorded legal name')).not.toBeInTheDocument();
    expect(screen.queryByText(/Historical display name/)).not.toBeInTheDocument(); expect(screen.queryByRole('heading', { name: 'Revision 1' })).not.toBeInTheDocument();
  });
  it('uses actual owning Board/List selections in the reviewed mutation rather than raw identifier inputs', async () => {
    const state = fixture(); mount(); await screen.findByLabelText(/^Legal name/);
    fireEvent.mouseDown(screen.getByLabelText('Intake Board')); fireEvent.click(await screen.findByRole('option', { name: 'Actual intake Board' }));
    await waitFor(() => expect(screen.getByLabelText('Intake List')).not.toHaveAttribute('aria-disabled', 'true'));
    fireEvent.mouseDown(screen.getByLabelText('Intake List')); fireEvent.click(await screen.findByRole('option', { name: 'Actual intake List' }));
    const dialog = await review(); expect(within(dialog).getByText('Actual intake Board')).toBeInTheDocument(); expect(within(dialog).getByText('Actual intake List')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Approve configuration change' })); await screen.findByText('Change acknowledged. Current configuration revision 2.');
    expect(JSON.parse(state.writes[0].body as string)).toMatchObject({ configuration: { intakeBoardId: boardId, intakeListId: listId } });
  });
  it('rechecks the selected Board after temporary intake failure without clearing the legal draft', async () => {
    const state = fixture(); state.boardUnavailable = true; mount();
    fireEvent.change(await screen.findByLabelText(/^Legal name/), { target: { value: 'Retained legal draft' } });
    fireEvent.mouseDown(screen.getByLabelText('Intake Board')); fireEvent.click(await screen.findByRole('option', { name: 'Actual intake Board' }));
    await screen.findByText(/The selected intake Board or its Lists are unavailable/);
    state.boardUnavailable = false;
    const retry = screen.getByRole('button', { name: 'Retry intake discovery' }); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
    await waitFor(() => expect(screen.queryByText(/The selected intake Board or its Lists are unavailable/)).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByLabelText('Intake List')).not.toHaveAttribute('aria-disabled', 'true'));
    fireEvent.mouseDown(screen.getByLabelText('Intake List')); fireEvent.click(await screen.findByRole('option', { name: 'Actual intake List' }));
    expect(screen.getByLabelText(/^Legal name/)).toHaveValue('Retained legal draft');
    const dialog = await review(); expect(within(dialog).getByText('Actual intake List')).toBeInTheDocument();
  });
});
