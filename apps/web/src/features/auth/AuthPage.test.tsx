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
    expect(screen.queryByLabelText('Display name')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Register' }));

    expect(screen.getByLabelText('Display name')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Create account' }),
    ).toBeInTheDocument();
  });
});
