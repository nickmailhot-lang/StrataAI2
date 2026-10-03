import { render, screen } from '@testing-library/react';
import { Dialog } from '@mui/material';
import { ownsRecoveryFocus } from './focusRecovery';

it('recognizes the installed MUI Dialog fallback but preserves other controls and dialogs', () => {
  render(<><Dialog open transitionDuration={0}><button>Owned action</button><button>Another action</button></Dialog>
    <div role="dialog" data-mui-focusable="" data-testid="other-dialog" /></>);
  const owner = screen.getByRole('button', { name: 'Owned action' });
  const paper = owner.closest('[role="dialog"]'); expect(paper).toHaveAttribute('data-mui-focusable');
  expect(ownsRecoveryFocus(paper, owner)).toBe(true);
  expect(ownsRecoveryFocus(document.body, owner)).toBe(true);
  expect(ownsRecoveryFocus(null, owner)).toBe(true);
  expect(ownsRecoveryFocus(screen.getByRole('button', { name: 'Another action' }), owner)).toBe(false);
  expect(ownsRecoveryFocus(screen.getByTestId('other-dialog'), owner)).toBe(false);
});
