import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationHome } from './OrganizationHome';

const live = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('../kanban/organizationBoardLive', () => ({ watchOrganizationBoards: live.watch }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: () => () => {} }));
vi.mock('./organizationLifecycleLive', () => ({ watchOrganizationLifecycle: () => () => {} }));
const org = '11111111-1111-4111-8111-111111111111';
const profile = { id: '22222222-2222-4222-8222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const organization = { organization: { id: org, name: 'Focus Organization', description: null, status: 0 }, role: 0 };
const response = (value: unknown) => new Response(JSON.stringify(value));
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

it.each(['Cancel', 'Escape'])('UXA-06: %s after access recovery returns focus to the current Board launcher', async action => {
  let held = false, finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (path: string) => {
    if (path === '/me') return response(profile);
    if (path === `/organizations/${org}`) return held
      ? new Promise<Response>(resolve => { finish = resolve; }) : response(organization);
    return response({ organizationId: org, items: [], nextCursor: null });
  }));
  const router = createMemoryRouter([{ path: '/app/:organizationId', element: <OrganizationHome /> }], { initialEntries: [`/app/${org}`] });
  render(<RouterProvider router={router} />);
  const original = await screen.findByRole('button', { name: 'Create board' });
  original.focus(); fireEvent.click(original);
  fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Unsent focus draft' } });
  held = true; act(() => live.watch.mock.calls[0][0].reset());
  await waitFor(() => expect(finish).toBeDefined());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  await act(async () => finish(response(organization)));
  const dialog = await screen.findByRole('dialog');
  expect(screen.getByLabelText(/Name/)).toHaveValue('Unsent focus draft');
  if (action === 'Cancel') fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
  else fireEvent.keyDown(dialog, { key: 'Escape' });
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  const current = await screen.findByRole('button', { name: 'Create board' });
  expect(current).not.toBe(original);
  await waitFor(() => expect(current).toHaveFocus());
});
