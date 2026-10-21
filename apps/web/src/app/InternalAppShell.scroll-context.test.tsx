import { act, render } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { AppThemeFixture } from '../test/AppThemeFixture';
import { InternalAppShell } from './InternalAppShell';

vi.mock('./SurfaceAdmission', () => ({ SurfaceAdmission: ({ children }: { children: React.ReactNode }) => children }));
vi.mock('./NavigationConfirmation', () => ({ NavigationConfirmation: () => null }));
vi.mock('./OrganizationContextLabel', () => ({ OrganizationContextLabel: () => <span>Organization: North garden</span> }));
vi.mock('../features/kanban/CardDescriptionPreview', () => ({ DescriptionParserWarmup: () => null }));

afterEach(() => vi.unstubAllGlobals());

function mount(path: string) {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  const scrollTo = vi.fn();
  vi.stubGlobal('scrollTo', scrollTo);
  const router = createMemoryRouter([{ path: '/app/:organizationId', element: <InternalAppShell />,
    children: [{ path: '*', element: <h2>Current workflow</h2> }] }], { initialEntries: [path] });
  render(<AppThemeFixture><RouterProvider router={router} /></AppThemeFixture>);
  return { router, scrollTo };
}

it('UXA-05/06: entering a Board from Organization home resets the previous page scroll', async () => {
  const { router, scrollTo } = mount('/app/north');
  await act(() => router.navigate('/app/north/boards/planning'));
  expect(scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'instant' });
});

it('UXA-04: opening and returning from Card details preserves the same Board document scroll', async () => {
  const { router, scrollTo } = mount('/app/north/boards/planning');
  await act(() => router.navigate('/app/north/boards/planning/cards/entrance'));
  await act(() => router.navigate(-1));
  expect(router.state.location.pathname).toBe('/app/north/boards/planning');
  expect(scrollTo).not.toHaveBeenCalled();
});

it('UXA-04/05: moving to another Board resets scroll while Card changes within it do not', async () => {
  const { router, scrollTo } = mount('/app/north/boards/planning/cards/entrance');
  await act(() => router.navigate('/app/north/boards/resolved/cards/lamp'));
  expect(scrollTo).toHaveBeenCalledExactlyOnceWith({ top: 0, left: 0, behavior: 'instant' });
  await act(() => router.navigate('/app/north/boards/resolved/cards/roof'));
  expect(scrollTo).toHaveBeenCalledTimes(1);
});

it('UXA-03/05: returning to Work or switching Organization resets only the page context', async () => {
  const { router, scrollTo } = mount('/app/north/boards/planning/cards/entrance');
  await act(() => router.navigate('/app/north'));
  expect(scrollTo).toHaveBeenCalledTimes(1);
  await act(() => router.navigate('/app/south'));
  expect(scrollTo).toHaveBeenCalledTimes(2);
  await act(() => router.navigate('/app/south?view=current#work'));
  expect(scrollTo).toHaveBeenCalledTimes(2);
});
