import { render, screen } from '@testing-library/react';
import { CardMemberIndicators } from './CardMemberIndicators';
const id = (n: number) => `44444444-4444-4444-4444-${String(n).padStart(12, '0')}`;
it('renders bounded initials with accessible full names and remaining count', () => {
  render(<CardMemberIndicators version={3} preview={{ cardVersion: 3, total: 9, items: Array.from({ length: 6 }, (_, i) => ({ userId: id(i + 1), displayName: `Taylor Member${i}` })) }} />);
  expect(screen.getByRole('group', { name: 'Card assignee indicators' })).toBeInTheDocument();
  expect(screen.getAllByRole('img')).toHaveLength(6); expect(screen.getByRole('img', { name: 'Assigned to Taylor Member0' })).toHaveTextContent('TM');
  expect(screen.getByText('+3 more assignees')).toBeInTheDocument();
});
it('handles unnamed members and Unicode initials', () => {
  render(<CardMemberIndicators version={3} preview={{ cardVersion: 3, total: 2, items: [{ userId: id(1), displayName: '' }, { userId: id(2), displayName: '😀 Rivera' }] }} />);
  expect(screen.getByRole('img', { name: 'Assigned to Unnamed member' })).toHaveTextContent('?');
  expect(screen.getByRole('img', { name: 'Assigned to 😀 Rivera' })).toHaveTextContent('😀R');
});
it('omits an older assignee preview alongside an acknowledged newer Card', () => {
  render(<CardMemberIndicators version={4} preview={{ cardVersion: 3, total: 1, items: [{ userId: id(1), displayName: 'Old member' }] }} />);
  expect(screen.queryByRole('img', { name: 'Assigned to Old member' })).not.toBeInTheDocument();
});
it.each([
  undefined, { cardVersion: 3, total: 0, items: [] }, { cardVersion: 3, total: 1, items: [] },
  { cardVersion: 3, total: 1, items: [{ userId: 'bad', displayName: 'Private name' }] },
  { cardVersion: 3, total: 2, items: [{ userId: id(1), displayName: 'Private name' }, { userId: id(1), displayName: 'Private name' }] },
  { cardVersion: 3, total: -1, items: [] },
])('omits missing, empty or invalid previews without leaking their names', preview => {
  render(<CardMemberIndicators version={3} preview={preview} />); expect(screen.queryByRole('group')).not.toBeInTheDocument(); expect(screen.queryByText('Private name')).not.toBeInTheDocument();
});
