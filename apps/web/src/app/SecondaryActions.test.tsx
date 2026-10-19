import { useEffect, useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { SecondaryActions } from './SecondaryActions';

const mode = vi.hoisted(() => ({ enabled: true }));
vi.mock('./workExperience', () => ({ get workExperienceEnabled() { return mode.enabled; } }));
beforeEach(() => { mode.enabled = true; });

it('PRD-03-VS-06-T-03: collapsing secondary actions preserves the mounted command owner and its draft', () => {
  const mounted = vi.fn();
  function DraftOwner() {
    const [value, setValue] = useState('Original review');
    useEffect(() => { mounted(); }, []);
    return <input aria-label="Pending review" value={value} onChange={event => setValue(event.target.value)} />;
  }
  render(<SecondaryActions label="Organization actions"><DraftOwner /></SecondaryActions>);
  const summary = screen.getByText('Organization actions', { selector: 'summary' });
  const disclosure = summary.parentElement as HTMLDetailsElement;
  expect(disclosure.open).toBe(false);
  fireEvent.click(summary); expect(disclosure.open).toBe(true);
  const draft = screen.getByLabelText('Pending review');
  fireEvent.change(draft, { target: { value: 'Retained explicit review' } });
  fireEvent.click(summary); expect(disclosure.open).toBe(false);
  fireEvent.click(summary); expect(disclosure.open).toBe(true);
  expect(screen.getByLabelText('Pending review')).toBe(draft);
  expect(draft).toHaveValue('Retained explicit review'); expect(mounted).toHaveBeenCalledTimes(1);
});

it('PRD-03-VS-06-T-03: rollback keeps permitted secondary actions inline', () => {
  mode.enabled = false;
  render(<SecondaryActions label="Organization actions"><a href="/settings">Organization settings</a></SecondaryActions>);
  expect(screen.queryByText('Organization actions', { selector: 'summary' })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Organization settings' })).toHaveAttribute('href', '/settings');
});
