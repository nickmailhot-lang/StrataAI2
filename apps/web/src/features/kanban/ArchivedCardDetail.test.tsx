import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ArchivedCardDetail, parseArchivedCardDetail } from './ArchivedCardDetail';
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(4), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const detail = { ...scope, title: 'Archived <script>🙂', description: 'Read-only notes', version: 2 };
const props = { ...scope, unavailable: false, refreshSequence: '1', reconnectSequence: 0, onDenied: vi.fn(), onRefresh: vi.fn() };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => vi.unstubAllGlobals());
it('reads scoped archived details, exposes comments and history without mutation controls, and purges immediately on invalidation', async () => {
  const requests: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input); requests.push(path);
    if (path === '/me') return reply(profile);
    if (path.endsWith('/archived-details')) return reply(detail);
    if (path.endsWith('/activity')) return reply({ ...scope, kind: 'CARD', targetId: scope.cardId, items: [], nextCursor: null });
    return reply({ ...scope, cardVersion: 2, items: [], nextCursor: null, canComment: false });
  }));
  const view = render(<MemoryRouter><ArchivedCardDetail {...props} /></MemoryRouter>);
  expect(await screen.findByText(detail.title)).toBeVisible(); expect(screen.queryByRole('textbox')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Review Card activity' }));
  await waitFor(() => expect(requests.some(path => path.endsWith('/activity'))).toBe(true));
  expect(screen.getByRole('button', { name: 'Review Card comments' })).toBeEnabled();
  view.rerender(<MemoryRouter><ArchivedCardDetail {...props} unavailable refreshSequence="2" /></MemoryRouter>);
  expect(screen.queryByText(detail.title)).toBeNull(); expect(screen.queryByRole('button', { name: 'Review Card activity' })).toBeNull();
});
it('fails closed for a mismatched scope and a changed authenticated account', async () => {
  expect(() => parseArchivedCardDetail({ ...detail, boardId: id(9) }, scope)).toThrow();
  let profiles = 0; const denied = vi.fn();
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => String(input) === '/me'
    ? reply({ ...profile, id: ++profiles === 1 ? profile.id : id(5) }) : reply(detail)));
  render(<MemoryRouter><ArchivedCardDetail {...props} onDenied={denied} /></MemoryRouter>);
  await waitFor(() => expect(denied).toHaveBeenCalled()); expect(screen.queryByText(detail.title)).toBeNull();
});
