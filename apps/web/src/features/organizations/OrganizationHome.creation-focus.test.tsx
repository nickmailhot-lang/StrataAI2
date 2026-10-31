import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationHome } from './OrganizationHome';

const streams = vi.hoisted(() => ({
  boards: vi.fn<(options: { invalidate(): void }) => () => void>(() => vi.fn()),
  metadata: vi.fn<(options: { invalidate(): void }) => () => void>(() => vi.fn()),
}));
vi.mock('../kanban/organizationBoardLive', () => ({ watchOrganizationBoards: streams.boards }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: streams.metadata }));
vi.mock('./organizationLifecycleLive', () => ({ watchOrganizationLifecycle: () => () => {} }));
const org = '11111111-1111-4111-8111-111111111111';
const profile = { id: '22222222-2222-4222-8222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const organization = { organization: { id: org, name: 'Retry focus Organization', description: null, status: 0 }, role: 0 };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

describe.each([
  ['boards', 'after-error'], ['metadata', 'after-error'],
  ['boards', 'while-pending'], ['metadata', 'while-pending'],
  ['boards', 'before-error'], ['metadata', 'before-error'],
] as const)('PRD-04-TC-06/07/09/11: %s admission %s', (stream, ordering) => {
  it.each([false, true])('restores the original retry control while respecting another focus destination (%s)', async elsewhere => {
    let held = false, finish!: (value: Response) => void, fail!: (reason: Error) => void;
    const writes: { body: BodyInit | null | undefined; key: string | null }[] = [];
    vi.stubGlobal('fetch', vi.fn(async (path: string, options?: RequestInit) => {
      if (path === '/me') return reply(profile);
      if (options?.method === 'POST' && path.startsWith('/boards?')) {
        writes.push({ body: options.body, key: new Headers(options.headers).get('Idempotency-Key') });
        return writes.length === 1 ? new Promise<Response>((_resolve, reject) => { fail = reject; }) : reply({ id: 'created-board' }, 201);
      }
      if (path === `/organizations/${org}`) return held ? new Promise<Response>(resolve => { finish = resolve; }) : reply(organization);
      return reply({ organizationId: org, items: [], nextCursor: null });
    }));
    const router = createMemoryRouter([
      { path: '/app/:organizationId', element: <OrganizationHome /> },
      { path: '/app/:organizationId/boards/:boardId', element: <h1>Canonical Board</h1> },
    ], { initialEntries: [`/app/${org}`] });
    render(<RouterProvider router={router} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Create board' }));
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Original keyboard draft' } });
    const submit = screen.getByRole('button', { name: 'Create' });
    submit.focus(); fireEvent.submit(submit.closest('form')!, { submitter: submit });
    await waitFor(() => expect(fail).toBeDefined());
    // Native disabled controls may leave focus on their own MUI trap paper.
    act(() => screen.getByRole('dialog').focus());
    if (ordering === 'after-error') {
      await act(async () => fail(new TypeError('Lost acknowledgment')));
      await screen.findByText(/Unable to confirm the save/);
    }
    held = true; act(() => streams[stream].mock.calls.at(-1)![0].invalidate());
    await waitFor(() => expect(finish).toBeDefined());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(writes).toHaveLength(1);
    const destination = document.createElement('button'); destination.textContent = 'Chosen independent destination';
    try {
      if (elsewhere) { document.body.appendChild(destination); destination.focus(); }
      if (ordering === 'while-pending') await act(async () => fail(new TypeError('Lost acknowledgment')));
      await act(async () => finish(reply(organization)));
      expect(await screen.findByRole('dialog')).toBeVisible();
      if (ordering === 'before-error') {
        expect(submit).toBeDisabled();
        await act(async () => fail(new TypeError('Lost acknowledgment')));
      }
      expect(screen.getByLabelText(/Name/)).toHaveValue('Original keyboard draft');
      expect(elsewhere ? destination : submit).toHaveFocus();
      expect(writes).toHaveLength(1);
      if (!elsewhere) {
        fireEvent.submit(submit.closest('form')!, { submitter: submit });
        await screen.findByRole('heading', { name: 'Canonical Board' });
        expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      }
    } finally { destination.remove(); }
  });
});
