import { screen, fireEvent } from '@testing-library/react';
import { renderRootFailure } from './rootFailure';

it('replaces the dead tree with focused fixed information and reloads only on explicit activation', () => {
  const host = document.createElement('div'); document.body.append(host);
  host.textContent = 'private-dead-tree';
  const reload = vi.fn();
  try {
    renderRootFailure(host, reload);
    expect(screen.getByRole('heading', { name: 'This application is unavailable.' })).toHaveFocus();
    expect(screen.getByRole('alert')).toHaveTextContent('Check the current state before trying again.');
    expect(document.body.textContent).not.toContain('private-dead-tree');
    expect(reload).not.toHaveBeenCalled();
    const oldButton = screen.getByRole('button', { name: 'Reload this page' });
    renderRootFailure(host, reload);
    expect(oldButton.isConnected).toBe(false);
    expect(screen.getAllByRole('main')).toHaveLength(1);
    expect(reload).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Reload this page' }));
    expect(reload).toHaveBeenCalledOnce();
  } finally { host.remove(); }
});
