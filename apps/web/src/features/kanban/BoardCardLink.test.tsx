import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { BoardCardLink } from './BoardCardLink';
import { BoardDateProvider } from './BoardDateBadges';
import { workRequest, type BoardSnapshot, type WorkCard } from '../../api/workManagement';
vi.mock('../../api/workManagement', async original => ({ ...await original<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
const card: WorkCard = { id: 'card', title: 'Inspect roof '.repeat(41).slice(0, 500), description: 'Long description stays in detail', version: 2, rank: 'a' };
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Work', description: null, lifecycleState: 'active' }, lists: [],
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false } };
it('UX-CARD-01/02 keeps the complete long title accessible, metadata explicit and description inside detail', () => {
  render(<MemoryRouter><BoardCardLink card={card} boardPath="/app/org/boards/board" links={new Map()}
    members={{ cardVersion: 2, total: 0, items: [] }} /></MemoryRouter>);
  expect(screen.getByRole('link', { name: card.title })).toHaveAttribute('href', '/app/org/boards/board/cards/card');
  expect(screen.getByText(card.title)).toHaveAttribute('title', card.title);
  expect(screen.getByText('Unassigned')).toBeInTheDocument();
  expect(screen.getByText('No due date')).toBeInTheDocument();
  expect(screen.queryByText(card.description!)).not.toBeInTheDocument();
});
it('UX-CARD-02 never reports a stale or absent member preview as unassigned', () => {
  render(<MemoryRouter><BoardCardLink card={card} boardPath="/board" links={new Map()}
    members={{ cardVersion: 1, total: 0, items: [] }} /></MemoryRouter>);
  expect(screen.queryByText('Unassigned')).not.toBeInTheDocument();
  expect(screen.getByText('Assignees unavailable')).toBeInTheDocument();
});
it('UX-CARD-03 shows a concrete due date and textual state from the existing timezone policy', async () => {
  vi.mocked(workRequest).mockResolvedValue({ id: '22222222-2222-2222-2222-222222222222', version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' });
  const dated = { ...card, startAt: null, dueAt: '2020-01-02T12:00:00Z', dueTimezone: 'UTC', dueHasTime: false, dueComplete: false };
  const datedSnapshot = { ...snapshot, lists: [{ list: { id: 'list', name: 'Work', rank: 'a', lifecycleState: 'active' }, cards: [dated] }] };
  render(<MemoryRouter><BoardDateProvider snapshot={datedSnapshot} unavailable={false} onRevalidate={vi.fn()}>
    <BoardCardLink card={dated} boardPath="/board" links={new Map()} />
  </BoardDateProvider></MemoryRouter>);
  expect(await screen.findByText('Jan 2, 2020')).toHaveAttribute('datetime', dated.dueAt);
  expect(screen.getByText('Overdue')).toBeInTheDocument();
});
