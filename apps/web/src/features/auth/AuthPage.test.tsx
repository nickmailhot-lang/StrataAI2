import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

import { AuthPage } from './AuthPage';

describe('PRD-02 authentication UI', () => {
  it('supports switching between sign-in and registration forms', () => {
    render(
      <MemoryRouter>
        <AuthPage />
      </MemoryRouter>,
    );

    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Create account' }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Register' }));

    expect(
      screen.getByRole('button', { name: 'Create account' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Use at least 12 characters/i),
    ).toBeInTheDocument();
  });
});
