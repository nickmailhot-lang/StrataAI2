import { render, screen } from '@testing-library/react';
import { Dialog } from '@mui/material';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';

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

it('parks only the activated owner in its own dialog and preserves subsequent user navigation', () => {
  render(<Dialog open transitionDuration={0}><button>Owned action</button><button>Another action</button></Dialog>);
  const owner = screen.getByRole('button', { name: 'Owned action' });
  const other = screen.getByRole('button', { name: 'Another action' });
  owner.focus(); parkRecoveryFocus(owner);
  expect(screen.getByRole('dialog')).toHaveFocus();
  other.focus(); parkRecoveryFocus(owner);
  expect(other).toHaveFocus(); expect(ownsRecoveryFocus(document.activeElement, owner)).toBe(false);
});

it('recognizes the same installed MUI trap container after an owned control loses focus', () => {
  render(<Dialog open transitionDuration={0}><button>Owned action</button><button>Another action</button></Dialog>);
  const owner = screen.getByRole('button', { name: 'Owned action' });
  const container = owner.closest<HTMLElement>('.MuiDialog-container[role="presentation"]')!;
  expect(container).toHaveAttribute('tabindex', '-1');
  owner.focus(); container.focus(); expect(container).toHaveFocus();
  expect(ownsRecoveryFocus(document.activeElement, owner)).toBe(true);
  const another = screen.getByRole('button', { name: 'Another action' });
  another.focus(); expect(ownsRecoveryFocus(document.activeElement, owner)).toBe(false);
  const foreign = document.createElement('div'); foreign.className = 'MuiDialog-container'; foreign.setAttribute('role', 'presentation');
  expect(ownsRecoveryFocus(foreign, owner)).toBe(false);
});
