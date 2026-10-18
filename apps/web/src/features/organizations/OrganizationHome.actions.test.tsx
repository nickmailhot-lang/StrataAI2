import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationHome } from './OrganizationHome';

const mode = vi.hoisted(() => ({ enabled: true }));
const metadata = vi.hoisted(() => ({ watch: vi.fn<(options: { reset(): void }) => () => void>(() => vi.fn()) }));
vi.mock('../../app/workExperience', () => ({ get workExperienceEnabled() { return mode.enabled; } }));
vi.mock('./organizationMetadataLive', () => ({ watchOrganizationMetadata: metadata.watch }));
vi.mock('../kanban/organizationBoardLive', () => ({ watchOrganizationBoards: () => () => {} }));
vi.mock('./organizationLifecycleLive', () => ({ watchOrganizationLifecycle: () => () => {} }));
const org = '11111111-1111-4111-8111-111111111111';
const profile = { id: '22222222-2222-4222-8222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const organization = { id: org, name: 'Current work Organization', description: null, status: 0 };
const response = (value: unknown) => new Response(JSON.stringify(value));
const memberActions = ['Archived boards', 'Leave Organization'];
const adminActions = ['Organization lifecycle', 'Organization settings', 'Organization configuration', 'Retention policies', 'Retention holds', 'Organization members'];
const expected = (role: number) => [...memberActions, ...(role <= 1 ? adminActions : []), ...(role === 0 ? ['Request Organization deletion'] : [])].sort();
function mount(read: () => Promise<Response> | Response) {
  const fetcher = vi.fn(async (path: string, _options?: RequestInit) => path === '/me' ? response(profile)
    : path === `/organizations/${org}` ? read() : response({ organizationId: org, items: [], nextCursor: null }));
  vi.stubGlobal('fetch', fetcher);
  const router = createMemoryRouter([{ path: '/app/:organizationId', element: <OrganizationHome /> }], { initialEntries: [`/app/${org}`] });
  render(<RouterProvider router={router} />); return fetcher;
}
async function openActions() {
  const summary = await screen.findByText('Organization actions', { selector: 'summary' });
  fireEvent.click(summary); expect(summary.parentElement).toHaveProperty('open', true); return summary.parentElement!;
}
const names = (element: Element) => Array.from(element.querySelectorAll('a')).map(link => link.textContent?.trim()).sort();
beforeEach(() => { mode.enabled = true; });
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

it.each([0, 1, 2])('PRD-03-VS-06-T-03: current role %s discloses exactly permitted existing Organization actions', async role => {
  mount(() => response({ organization, role }));
  await screen.findByRole('button', { name: 'Create board' });
  const summary = await screen.findByText('Organization actions', { selector: 'summary' });
  expect(summary.parentElement).toHaveProperty('open', false);
  const disclosure = await openActions(); expect(names(disclosure)).toEqual(expected(role));
  for (const link of disclosure.querySelectorAll('a')) expect(link.getAttribute('href')).toMatch(new RegExp(`^/app/${org}/`));
  fireEvent.click(summary); expect(disclosure).toHaveProperty('open', false);
});

it('PRD-03-VS-06-T-03: Create Board precedes secondary operations in keyboard/reading order', async () => {
  mount(() => response({ organization, role: 0 }));
  const create = await screen.findByRole('button', { name: 'Create board' });
  expect(create.parentElement?.firstElementChild).toBe(create);
});

it('PRD-03-VS-06-T-03: admission withdrawal removes the entire old disclosure before a fresh downgraded role is confirmed', async () => {
  let held = false, finish!: (value: Response) => void;
  mount(() => held ? new Promise<Response>(resolve => { finish = resolve; }) : response({ organization, role: 0 }));
  const original = await openActions(); expect(names(original)).toEqual(expected(0));
  await waitFor(() => expect(metadata.watch).toHaveBeenCalled());
  held = true; act(() => metadata.watch.mock.calls.at(-1)![0].reset());
  expect(screen.queryByText('Organization actions', { selector: 'summary' })).not.toBeInTheDocument();
  expect(screen.queryByText('Organization settings', { selector: 'a' })).not.toBeInTheDocument();
  expect(screen.queryByText('Request Organization deletion', { selector: 'a' })).not.toBeInTheDocument();
  await waitFor(() => expect(finish).toBeDefined());
  await act(async () => finish(response({ organization, role: 2 })));
  await screen.findByRole('button', { name: 'Create board' });
  const current = (await screen.findByText('Organization actions', { selector: 'summary' })).parentElement!;
  expect(current).not.toBe(original); expect(current).toHaveProperty('open', true); expect(names(current)).toEqual(expected(2));
});

it('PRD-03-VS-06-T-03: rollback retains inline role-permitted actions and the existing Board creation/cancel workflow', async () => {
  mode.enabled = false; const fetcher = mount(() => response({ organization, role: 0 }));
  const create = await screen.findByRole('button', { name: 'Create board' });
  expect(screen.queryByText('Organization actions', { selector: 'summary' })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Organization settings' })).toHaveAttribute('href', `/app/${org}/settings`);
  fireEvent.click(create); fireEvent.change(await screen.findByLabelText(/Name/), { target: { value: 'Unsent original draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(create).toHaveFocus());
  expect(fetcher.mock.calls.every(([, options]) => !options?.method || options.method === 'GET')).toBe(true);
});
