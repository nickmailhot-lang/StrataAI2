import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createTheme, ThemeProvider } from '@mui/material';
import { createMemoryRouter, Link, RouterProvider } from 'react-router-dom';
import { SurfaceAdmission } from './SurfaceAdmission';
import { UnsavedChangesGuard } from './UnsavedChangesGuard';
import type { watchOperationalLifecycle } from '../features/organizations/operationalLifecycleLive';

const live = vi.hoisted(() => ({ options: undefined as Parameters<typeof watchOperationalLifecycle>[0] | undefined }));
vi.mock('../features/organizations/operationalLifecycleLive', () => ({
  watchOperationalLifecycle: (options: Parameters<typeof watchOperationalLifecycle>[0]) => { live.options = options; return vi.fn(); },
}));
const organization = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const path = `/app/${organization}/boards/33333333-3333-4333-8333-333333333333/cards/44444444-4444-4444-8444-444444444444`;
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' };
const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
beforeEach(() => { live.options = undefined; });
afterEach(() => vi.unstubAllGlobals());

it.each([false, true])('PRD-25-VS-02-T-02/AC-02: actual surface withdrawal retires a dirty draft and reaches fresh sign-in with pending discard=%s', async pending => {
  let gone = false;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url === '/me'
    ? gone ? reply({}, 401) : reply(profile) : reply({ organizationId: organization, surface: 'INTERNAL' })));
  const router = createMemoryRouter([
    { path: '/app/:organizationId/boards/:boardId/*', element: <SurfaceAdmission surface="INTERNAL" deniedContent={<p>Independent Board view</p>}>
      <UnsavedChangesGuard dirty /><input aria-label="Private Card draft" defaultValue="Keep this unsaved private title" /><Link to="/app">Open organizations</Link>
    </SurfaceAdmission> },
    { path: '/app', element: <h1>Organizations</h1> },
    { path: '/login', element: <h1>Fresh sign-in required</h1> },
  ], { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  await screen.findByLabelText('Private Card draft'); await waitFor(() => expect(live.options).toBeDefined());
  if (pending) {
    await act(async () => { await router.navigate('/app'); });
    await screen.findByRole('dialog', { name: 'Discard unsaved changes?' });
  }
  gone = true; act(() => live.options!.denied(401, false));
  expect(screen.queryByDisplayValue('Keep this unsaved private title')).not.toBeInTheDocument();
  await screen.findByRole('heading', { name: 'Fresh sign-in required' });
  expect(router.state.location.pathname).toBe('/login');
  expect(router.state.location.state).toEqual({ returnTo: path });
  expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument();
});

it('PRD-25-VS-02-T-02/AC-02: generic choices keep modal keyboard focus under the actual uncertain-admission defaults', async () => {
  const withheld = document.createElement('div'); withheld.hidden = true; document.body.append(withheld);
  const outside = document.createElement('button'); outside.textContent = 'Access recovery'; document.body.append(outside);
  const foreground = vi.spyOn(document, 'hasFocus').mockReturnValue(true);
  const theme = createTheme({ components: { MuiModal: { defaultProps: {
    container: withheld, disableAutoFocus: true, disableEnforceFocus: true,
  } } } });
  const router = createMemoryRouter([
    { path: '/card', element: <><ThemeProvider theme={theme}><div hidden>
      <UnsavedChangesGuard dirty /><input aria-label="Withheld draft" defaultValue="Private unsaved title" />
    </div></ThemeProvider><Link to="/work">Open Work</Link></> },
    { path: '/work', element: <h1>Work</h1> },
  ], { initialEntries: ['/card'] });
  try {
    render(<RouterProvider router={router} />);
    fireEvent.click(screen.getByRole('link', { name: 'Open Work' }));
    const review = await screen.findByRole('dialog', { name: 'Discard unsaved changes?' });
    expect(review).toBeVisible(); expect(screen.getByLabelText('Withheld draft')).not.toBeVisible();
    act(() => outside.focus());
    await waitFor(() => expect(review.contains(document.activeElement), JSON.stringify({
      tag: document.activeElement?.tagName, role: document.activeElement?.getAttribute('role'),
      className: document.activeElement?.className,
    })).toBe(true));
    expect(review).not.toHaveTextContent('Private unsaved title');
  } finally { foreground.mockRestore(); outside.remove(); withheld.remove(); }
});
