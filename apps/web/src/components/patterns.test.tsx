import { createRef } from 'react';
import { Alert, Button, TextField } from '@mui/material';
import { fireEvent, render, screen } from '@testing-library/react';
import { AppThemeFixture } from '../test/AppThemeFixture';
import { ActionRow } from './ActionRow';
import { PageHeader } from './PageHeader';

it('UX-003: shared headings retain the owning page hierarchy and complete long content', () => {
  const description = 'A'.repeat(240);
  const { rerender } = render(<AppThemeFixture><PageHeader title="Search Cards" description={description} variant="h5" /></AppThemeFixture>);
  expect(screen.getByRole('heading', { level: 2, name: 'Search Cards' })).toBeVisible();
  expect(screen.getByText(description)).toHaveTextContent(description);
  rerender(<AppThemeFixture><PageHeader title="Account" level="h1" /></AppThemeFixture>);
  expect(screen.getByRole('heading', { level: 1, name: 'Account' })).toBeVisible();
});

it('UX-003: layout preserves action refs, explicit pending/disabled state and retry ownership', () => {
  const button = createRef<HTMLButtonElement>(); const submit = vi.fn();
  const controls = (busy: boolean) => <AppThemeFixture><>
    <PageHeader title="Review" />
    <ActionRow component="form" onSubmit={event => { event.preventDefault(); submit(); }}>
      <TextField label="Canonical name" defaultValue="Original draft" />
      <Button ref={button} type="submit" disabled={busy}>Confirm review</Button>
    </ActionRow>
    {busy ? <p role="status">Checking original outcome</p> : <Alert severity="error">Review the current state before retrying</Alert>}
  </></AppThemeFixture>;
  const { rerender } = render(controls(true));
  expect(button.current).toBe(screen.getByRole('button', { name: 'Confirm review' }));
  expect(button.current).toBeDisabled(); expect(screen.getByRole('status')).toHaveTextContent('Checking original outcome');
  rerender(controls(false));
  expect(screen.getByRole('textbox', { name: 'Canonical name' })).toHaveValue('Original draft');
  expect(screen.getByRole('alert')).toHaveTextContent('Review the current state');
  button.current?.focus(); expect(button.current).toHaveFocus();
  fireEvent.submit(screen.getByRole('button', { name: 'Confirm review' }).closest('form')!);
  expect(submit).toHaveBeenCalledTimes(1);
});
