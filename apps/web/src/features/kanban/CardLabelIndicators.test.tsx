import { render, screen } from '@testing-library/react';
import { CardLabelIndicators } from './CardLabelIndicators';
it('provides text and color descriptions with an explicit remaining count', () => {
  render(<CardLabelIndicators preview={{ items: [{ id: 'one', name: 'Priority', color: 'red' }, { id: 'two', name: '', color: 'blue' }], total: 8 }} />);
  expect(screen.getByLabelText('Priority, red')).toBeVisible(); expect(screen.getByText('blue label')).toBeVisible(); expect(screen.getByText('+6 more labels')).toBeVisible();
});
it('does not add interactive controls inside the Card link', () => {
  render(<CardLabelIndicators preview={{ items: [{ id: 'one', name: 'Priority', color: 'black' }], total: 1 }} />);
  expect(screen.queryByRole('button')).not.toBeInTheDocument(); expect(screen.queryByRole('link')).not.toBeInTheDocument();
});
it('omits malformed and empty previews', () => {
  const view = render(<CardLabelIndicators preview={{ items: [{ id: 'one', name: 'Invalid', color: 'unknown' }], total: 1 }} />);
  expect(screen.queryByText('Invalid')).not.toBeInTheDocument();
  view.rerender(<CardLabelIndicators preview={{ items: [], total: 0 }} />); expect(screen.queryByLabelText('Card label indicators')).not.toBeInTheDocument();
});
