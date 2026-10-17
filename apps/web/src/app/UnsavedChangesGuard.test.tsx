import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, Link, RouterProvider } from 'react-router-dom';
import { UnsavedChangesGuard } from './UnsavedChangesGuard';

it('UX-DETAIL-02 preserves a draft across canceled navigation and requires explicit discard to leave', async () => {
  const router = createMemoryRouter([
    { path: '/card', element: <><UnsavedChangesGuard dirty /><input aria-label="Draft title" defaultValue="Repair drain" /><Link to="/work">Close</Link></> },
    { path: '/work', element: <h1>Work</h1> },
  ], { initialEntries: ['/card'] });
  render(<RouterProvider router={router} />);
  fireEvent.click(screen.getByRole('link', { name: 'Close' }));
  expect(await screen.findByRole('dialog', { name: 'Discard unsaved changes?' })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.getByLabelText('Draft title')).toHaveValue('Repair drain');
  const beforeUnload = new Event('beforeunload', { cancelable: true });
  window.dispatchEvent(beforeUnload); expect(beforeUnload.defaultPrevented).toBe(true);
  fireEvent.click(screen.getByRole('link', { name: 'Close' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Discard changes and leave' }));
  await screen.findByRole('heading', { name: 'Work' });
});
