import { render, screen } from '@testing-library/react';

import { App } from './App';

describe('StrataAI2 application shell', () => {
  it('renders the internal application shell', async () => {
    window.history.pushState({}, '', '/app/demo/boards/demo-board');

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'Council Operations' }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Organization: demo/)).toBeInTheDocument();
  });

  it('keeps the owner portal visually and navigationally separate', async () => {
    window.history.pushState({}, '', '/portal/demo');

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: 'Owner documents' }),
    ).toBeInTheDocument();
    expect(screen.queryByText('Council Operations')).not.toBeInTheDocument();
  });
});
