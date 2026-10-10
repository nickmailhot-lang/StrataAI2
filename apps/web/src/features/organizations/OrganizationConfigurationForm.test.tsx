import { fireEvent, render, screen } from '@testing-library/react';
import { AppThemeFixture } from '../../test/AppThemeFixture';
import { OrganizationConfigurationForm } from './OrganizationConfigurationForm';
import { configurationDraft } from './organizationConfigurationDraft';

it('preserves newer sibling values when an unchanged legal field is edited', () => {
  const draft = { ...configurationDraft(), legalName: 'Legal corporation', jurisdiction: 'BC', timezone: 'UTC' };
  const onChange = vi.fn(); const onBoardChange = vi.fn();
  const props = { onChange, onBoardChange, boards: [], lists: [], disabled: false };
  const view = render(<OrganizationConfigurationForm {...props} draft={draft} />, { wrapper: AppThemeFixture });
  const newer = { ...draft, jurisdiction: 'AB', timezone: 'America/Edmonton', lotCount: '40' };
  view.rerender(<OrganizationConfigurationForm {...props} draft={newer} />);
  fireEvent.change(screen.getByLabelText(/^Legal name/), { target: { value: 'Updated corporation' } });
  expect(onChange).toHaveBeenCalledExactlyOnceWith({ ...newer, legalName: 'Updated corporation' });
});

it('withdraws scalar editing during review and restores it against the latest draft', () => {
  const draft = { ...configurationDraft(), legalName: 'Review source', jurisdiction: 'BC', timezone: 'UTC' };
  const onChange = vi.fn(); const onBoardChange = vi.fn();
  const props = { draft, onChange, onBoardChange, boards: [], lists: [] };
  const view = render(<OrganizationConfigurationForm {...props} disabled={false} />, { wrapper: AppThemeFixture });
  view.rerender(<OrganizationConfigurationForm {...props} disabled />);
  for (const input of screen.getAllByRole('textbox')) expect(input).toBeDisabled();
  // A synthetic event bypasses the browser's disabled-control behavior.
  fireEvent.change(screen.getByLabelText(/^Legal name/), { target: { value: 'Unreviewed replacement' } });
  expect(onChange).not.toHaveBeenCalled();
  const latest = { ...draft, jurisdiction: 'AB' };
  view.rerender(<OrganizationConfigurationForm {...props} draft={latest} disabled={false} />);
  expect(screen.getByLabelText(/^Legal name/)).toBeEnabled();
  fireEvent.change(screen.getByLabelText(/^Legal name/), { target: { value: 'Next proposal' } });
  expect(onChange).toHaveBeenCalledExactlyOnceWith({ ...latest, legalName: 'Next proposal' });
});
