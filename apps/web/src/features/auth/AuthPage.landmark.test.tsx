import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AuthPage } from './AuthPage';

// PRD-02-TC-11/12, UXA-06: preserve the existing account-entry controls in main.
it.each([false, true])('contains account entry in one main landmark (invitation callback: %s)', invitation => {
  render(<MemoryRouter><AuthPage onAuthenticated={invitation ? () => {} : undefined} /></MemoryRouter>);
  const main = screen.getByRole('main');
  expect(screen.getAllByRole('main')).toHaveLength(1);
  expect(within(main).getByRole('heading', { name: 'StrataAI2', level: 1 })).toBeVisible();
  expect(within(main).getByRole('textbox', { name: 'Email' })).toBeVisible();
  expect(within(main).getByLabelText(/^Password/)).toBeVisible();
  expect(within(main).getByRole('button', { name: 'Sign in' })).toBeVisible();
  expect(within(main).getByRole('link', { name: 'Forgot password?' })).toHaveAttribute('href', '/forgot-password');
});
