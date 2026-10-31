import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { StrataLotDirectoryPage } from './StrataLotDirectoryPage';
import { emptyLot, LotIntent, retainLotIntent } from './strataLotClient';
const live = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: live.watch }));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222', lotId = '33333333-3333-4333-8333-333333333333';
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
afterEach(() => { vi.unstubAllGlobals(); sessionStorage.clear(); vi.clearAllMocks(); });
it.each([0, 1].flatMap(version => ['before-retry-commit', 'during-retry', 'duplicate-native-activation', 'foreground-before-commit'].map(order => ({ version, order }))))('PRD-28-TC-07/08/09/16: metadata $order and restored version $version refusal readmit the preserved draft', async ({ version, order }) => {
  const data = { ...emptyLot, unitIdentifier: '101A', lotIdentifier: 'SL1' };
  retainLotIntent(new LotIntent(org, actor, lotId, version, data));
  let refuse: (() => void) | undefined;
  const writes: RequestInit[] = [];
  vi.stubGlobal('fetch', async (path: string, options: RequestInit = {}) => {
    if (path === '/me') return reply({ id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' });
    if (path === `/organizations/${org}`) return reply({ organization: { id: org, status: 0 }, role: 0 });
    if (options.method === 'PATCH') { writes.push(options); return new Promise<Response>(resolve => { refuse = () => resolve(reply({ code: 'version_conflict' }, 409)); }); }
    const lot = { id: lotId, organizationId: org, version: version + 1, data: { ...data, unitIdentifier: '101B' }, createdAt: '2026-10-28T00:00:00Z', updatedAt: '2026-10-28T00:00:00Z' };
    if (path.endsWith(`/${lotId}`)) return reply(lot);
    return reply({ organizationId: org, items: [lot], nextCursor: null });
  });
  const router = createMemoryRouter([{ path: '/app/:organizationId/property/units', element: <StrataLotDirectoryPage /> }], { initialEntries: [`/app/${org}/property/units`] });
  render(<RouterProvider router={router} />);
  const retry = await screen.findByRole('button', { name: 'Retry original unit change' });
  await act(async () => {
    if (order === 'before-retry-commit') live.watch.mock.calls[0][0].reset();
    fireEvent.click(retry);
    if (order === 'duplicate-native-activation') fireEvent.click(retry);
    if (order === 'foreground-before-commit') window.dispatchEvent(new Event('focus'));
  });
  if (order === 'before-retry-commit') {
    // The native event used the previous render, but a protected read already
    // owns admission. It must dispatch no write; the next explicit activation
    // after readmission still uses the retained original.
    expect(writes).toHaveLength(0);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry original unit change' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Retry original unit change' }));
  }
  await waitFor(() => expect(refuse).toBeDefined());
  if (order === 'during-retry') await act(async () => live.watch.mock.calls[0][0].reset());
  await act(async () => refuse!());
  await screen.findByText(/Your draft is preserved. Refresh current records/);
  expect(screen.getByLabelText(/^Unit identifier/)).toHaveValue('101A');
  expect(writes).toHaveLength(1);
  expect(screen.queryByRole('button', { name: 'Retry original unit change' })).not.toBeInTheDocument();
});
