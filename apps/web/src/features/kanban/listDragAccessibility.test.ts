import { expect, it } from 'vitest';
import type { BoardSnapshot } from '../../api/workManagement';
import { listDragAnnouncements } from './listDragAccessibility';

const snapshot: BoardSnapshot = {
  board: { id: 'board', organizationId: 'org', name: 'Board', description: '', lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: true },
  lists: ['Planning', 'Complete', 'Archived'].map((name, index) => ({
    list: { id: `internal-${index}`, name, rank: String(index), lifecycleState: index === 2 ? 'archived' : 'active' }, cards: [],
  })),
};
const announcements = listDragAnnouncements(snapshot);
function event(target: string | null = 'internal-1', source = 'internal-0') {
  return { active: { id: source }, over: target ? { id: target } : null } as Parameters<typeof announcements.onDragOver>[0];
}
it('announces named positions and a request without claiming persistence', () => {
  expect(announcements.onDragStart(event())).toContain('Dragging Planning list');
  expect(announcements.onDragOver(event())).toBe('Planning list can be dropped before Complete.');
  expect(announcements.onDragOver(event('list-end'))).toContain('at the end of the Board');
  expect(announcements.onDragEnd(event())).toBe('Drop requested for Planning list before Complete. Check the move status for confirmation.');
});
it('announces non-writing cancellation and outside drops without exposing unknown or archived IDs', () => {
  expect(announcements.onDragCancel(event())).toBe('Drag cancelled. Planning list was not moved.');
  expect(announcements.onDragEnd(event(null))).toBe('Drag ended. Planning list was not moved.');
  expect(announcements.onDragOver(event('internal-2'))).toBeUndefined();
  expect(announcements.onDragStart(event(null, 'unknown-private-id'))).toBeUndefined();
});
