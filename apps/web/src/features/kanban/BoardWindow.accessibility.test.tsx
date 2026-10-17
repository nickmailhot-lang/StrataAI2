import { render, screen } from '@testing-library/react';
import { BoardWindow } from './BoardWindow';

vi.mock('@dnd-kit/core', () => ({ useDndContext: () => ({ active: null, measureDroppableContainers: vi.fn() }) }));

it('UX-001: exposes the empty Board canvas as a named region for assistive technology', () => {
  render(<BoardWindow items={[]} axis="lists" memory={new Map()} memoryKey="empty" renderItem={() => null} />);
  expect(screen.getByRole('region', { name: 'Kanban board' })).toBeVisible();
});
