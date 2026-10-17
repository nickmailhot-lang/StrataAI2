import { act, fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { AppThemeFixture } from '../test/AppThemeFixture';
import { InternalAppShell } from './InternalAppShell';

vi.mock('./SurfaceAdmission', () => ({ SurfaceAdmission: ({ children }: { children: React.ReactNode }) => children }));
vi.mock('./NavigationConfirmation', () => ({ NavigationConfirmation: () => null }));
vi.mock('../features/kanban/CardDescriptionPreview', () => ({ DescriptionParserWarmup: () => null }));

afterEach(() => vi.unstubAllGlobals());

function mount(path = '/app/demo/boards/board/cards/card') {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  const router = createMemoryRouter([{ path: '/app/:organizationId', element: <InternalAppShell />,
    children: [{ path: '*', element: <h2>Current workflow</h2> }] }], { initialEntries: [path] });
  render(<AppThemeFixture><RouterProvider router={router} /></AppThemeFixture>);
  return router;
}

it('UX-001-AC-02: shares destinations and current Board context in the mobile drawer', async () => {
  const router = mount();
  expect(within(screen.getByRole('navigation')).getByRole('link', { name: 'Work' })).toHaveAttribute('aria-current', 'page');
  const trigger = screen.getByRole('button', { name: 'Open navigation' });
  fireEvent.click(trigger);
  const mobile = screen.getByRole('navigation', { name: 'Mobile application navigation' });
  expect(trigger).toHaveAttribute('aria-expanded', 'true');
  expect(within(mobile).getByRole('link', { name: 'Organizations' })).toHaveAttribute('href', '/app');
  expect(within(mobile).getByRole('link', { name: 'Work' })).toHaveAttribute('href', '/app/demo');
  fireEvent.click(within(mobile).getByRole('link', { name: 'Notifications' }));
  await waitFor(() => expect(router.state.location.pathname).toBe('/app/demo/notifications'));
  await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Mobile application navigation' })).toBeNull());
  expect(within(screen.getByRole('navigation')).getByRole('link', { name: 'Notifications' })).toHaveAttribute('aria-current', 'page');
  expect(within(screen.getByRole('navigation')).getByRole('link', { name: 'Work' })).not.toHaveAttribute('aria-current');
});

it('UX-001-AC-03: skip navigation focuses the single main landmark', () => {
  const router = mount();
  fireEvent.click(screen.getByRole('link', { name: 'Skip to main content' }));
  expect(screen.getByRole('main')).toHaveFocus();
  expect(screen.getAllByRole('main')).toHaveLength(1);
  expect(router.state.location.hash).toBe('');
});

it('UX-001-AC-02: closing navigation preserves the deep link', async () => {
  const router = mount();
  fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
  fireEvent.click(screen.getByRole('button', { name: 'Close navigation' }));
  await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Mobile application navigation' })).toBeNull());
  expect(router.state.location.pathname).toBe('/app/demo/boards/board/cards/card');
});


it('UX-002: unavailable modules are omitted under the live Work-navigation amendment', () => {
  mount();
  for (const name of ['Meetings', 'Correspondence', 'Documents', 'Owners & Units']) {
    expect(screen.queryByRole('link', { name })).not.toBeInTheDocument();
    expect(screen.queryByText(name, { exact: true })).not.toBeInTheDocument();
  }
  expect(screen.queryByText('Not available yet')).not.toBeInTheDocument();
});


it('UX-003: content offset follows the actual header after text or viewport reflow', () => {
  let notify = () => {};
  vi.stubGlobal('ResizeObserver', class {
    constructor(callback: () => void) { notify = callback; }
    observe() {}
    disconnect() {}
  });
  mount();
  const header = screen.getByRole('banner');
  const measure = vi.spyOn(header, 'getBoundingClientRect');
  measure.mockReturnValue({ height: 136 } as DOMRect);
  act(() => notify());
  expect(screen.getByRole('main').firstElementChild).toHaveStyle({ height: '136px' });
  measure.mockReturnValue({ height: 208 } as DOMRect);
  act(() => notify());
  expect(screen.getByRole('main').firstElementChild).toHaveStyle({ height: '208px' });
});
