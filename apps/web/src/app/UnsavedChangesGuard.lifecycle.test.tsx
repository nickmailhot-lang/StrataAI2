import { useEffect, useLayoutEffect, useState } from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { createTheme, ThemeProvider } from '@mui/material';
import { createMemoryRouter, Link, RouterProvider, useNavigate } from 'react-router-dom';
import { UnsavedChangesGuard } from './UnsavedChangesGuard';

it('UX-DETAIL-02 releases a pending destination after the original save acknowledges a clean draft', async () => {
  let acknowledge: () => void = () => { throw new Error('Card not mounted'); };
  function Card() {
    const [dirty, setDirty] = useState(true);
    useLayoutEffect(() => { acknowledge = () => setDirty(false); }, []);
    return <><UnsavedChangesGuard dirty={dirty} /><Link to="/work">Close</Link></>;
  }
  const router = createMemoryRouter([
    { path: '/card', element: <Card /> }, { path: '/work', element: <h1>Work</h1> },
  ], { initialEntries: ['/card'] });
  render(<RouterProvider router={router} />);
  fireEvent.click(screen.getByRole('link', { name: 'Close' }));
  await screen.findByRole('dialog', { name: 'Discard unsaved changes?' });
  act(() => acknowledge());
  await screen.findByRole('heading', { name: 'Work' });
  expect(router.state.location.pathname).toBe('/work');
  const unload = new Event('beforeunload', { cancelable: true });
  window.dispatchEvent(unload); expect(unload.defaultPrevented).toBe(false);
});

it('UXA-03 cannot retain a private draft or block sign-in after definitive access withdrawal', async () => {
  let withdraw: () => void = () => { throw new Error('Boundary not mounted'); };
  function Boundary() {
    const [admitted, setAdmitted] = useState(true), navigate = useNavigate();
    useLayoutEffect(() => { withdraw = () => setAdmitted(false); }, []);
    useEffect(() => { if (!admitted) void navigate('/login', { replace: true }); }, [admitted, navigate]);
    return admitted ? <><UnsavedChangesGuard dirty /><input aria-label="Private title" defaultValue="Retained private draft" /><Link to="/work">Close</Link></> : <p>Access withdrawn</p>;
  }
  const router = createMemoryRouter([
    { path: '/card', element: <Boundary /> }, { path: '/work', element: <h1>Work</h1> },
    { path: '/login', element: <h1>Sign in</h1> },
  ], { initialEntries: ['/card'] });
  render(<RouterProvider router={router} />);
  fireEvent.click(screen.getByRole('link', { name: 'Close' }));
  await screen.findByRole('dialog', { name: 'Discard unsaved changes?' });
  act(() => withdraw());
  await screen.findByRole('heading', { name: 'Sign in' });
  expect(screen.queryByDisplayValue('Retained private draft')).not.toBeInTheDocument();
  expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument();
  expect(router.state.location.pathname).toBe('/login');
});

it('UXA-03/05 keeps generic discard choices operable when an uncertain admission hides private content', async () => {
  const withheld = document.createElement('div'); withheld.hidden = true; document.body.append(withheld);
  const theme = createTheme({ components: { MuiModal: { defaultProps: { container: withheld } } } });
  const router = createMemoryRouter([
    { path: '/card', element: <><ThemeProvider theme={theme}><div hidden>
      <UnsavedChangesGuard dirty /><input aria-label="Private title" defaultValue="Withheld private draft" />
    </div></ThemeProvider><Link to="/work">Open Work</Link></> },
    { path: '/work', element: <h1>Work</h1> },
  ], { initialEntries: ['/card'] });
  try {
    render(<RouterProvider router={router} />);
    fireEvent.click(screen.getByRole('link', { name: 'Open Work' }));
    const review = await screen.findByRole('dialog', { name: 'Discard unsaved changes?' });
    expect(review).toBeVisible(); expect(review).not.toHaveTextContent('Withheld private draft');
    expect(screen.getByLabelText('Private title')).not.toBeVisible();
    fireEvent.click(within(review).getByRole('button', { name: 'Discard changes and leave' }));
    await screen.findByRole('heading', { name: 'Work' });
    expect(screen.queryByDisplayValue('Withheld private draft')).not.toBeInTheDocument();
  } finally { withheld.remove(); }
});
