import { parseChecklistItemPage, parseChecklistPage, type Checklist, type ChecklistItem, type ChecklistScope } from './checklists';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const rank = (n: number) => String(n).padStart(30, '0');
const scope: ChecklistScope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const now = '2026-10-03T01:00:00.123456Z';
const parent: Checklist = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, title: 'Preparations', rank: rank(1),
  createdAt: now, updatedAt: now, version: 2, deletedAt: null };
const item: ChecklistItem = { id: id(5), organizationId: scope.organizationId, checklistId: parent.id, text: 'Prepare', rank: rank(1),
  completed: false, completedAt: null, completedBy: null, createdAt: now, updatedAt: now, version: 3, deletedAt: null };
const progress = () => ({ checklist: { ...parent }, completed: 0, total: 1, percent: 0 });
const page = () => ({ ...scope, cardVersion: 4, canEdit: false, items: [progress()], nextCursor: null });
const items = () => ({ ...scope, cardVersion: 4, canEdit: true, summary: progress(), items: [{ ...item }], nextCursor: null });

it('admits scoped read-only Checklist pages and canonical empty progress', () => {
  expect(parseChecklistPage(page(), scope).canEdit).toBe(false);
  const empty = page(); empty.items[0].total = 0;
  expect(parseChecklistPage(empty, scope).items[0].percent).toBe(0);
  expect(parseChecklistItemPage(items(), scope, parent.id).items[0].text).toBe('Prepare');
});
it('validates bounded seek pages, canonical last-row cursors and full progress independent of the page', () => {
  const value = items(); value.items = Array.from({ length: 50 }, (_, n) => ({ ...item, id: id(n + 20), rank: rank(n + 1) }));
  const response = { ...value, summary: { ...progress(), total: 63, completed: 1, percent: 100 / 63 }, nextCursor: `${parent.id}/${rank(50)}/${id(69)}` };
  expect(parseChecklistItemPage(response, scope, parent.id).summary.total).toBe(63);
  const second = { ...response, items: Array.from({ length: 13 }, (_, n) => ({ ...item, id: id(n + 70), rank: rank(n + 51) })), nextCursor: null };
  expect(parseChecklistItemPage(second, scope, parent.id, response.nextCursor).items).toHaveLength(13);
  expect(() => parseChecklistItemPage(second, scope, parent.id, `${scope.cardId}/${rank(50)}/${id(69)}`)).toThrow();
});
it('preserves UTC microseconds and requires complete attribution inside item lifetime', () => {
  const value = items(); value.items[0] = { ...item, completed: true, completedAt: now, completedBy: id(9) };
  value.summary = { ...progress(), completed: 1, percent: 100 };
  expect(parseChecklistItemPage(value, scope, parent.id).items[0].completedAt).toBe(now);
  value.items[0].completedAt = '2026-10-03T01:00:00.123457Z';
  expect(() => parseChecklistItemPage(value, scope, parent.id)).toThrow();
});
it.each([
  { organizationId: id(90) }, { boardId: id(90) }, { cardId: id(90) }, { cardVersion: 0 },
  { cardVersion: Number.MAX_SAFE_INTEGER + 1 }, { canEdit: undefined }, { items: null },
  { nextCursor: `${scope.cardId}/${rank(1)}/${parent.id}` }, { nextCursor: undefined },
])('rejects malformed or foreign page admission (%j)', change => {
  expect(() => parseChecklistPage({ ...page(), ...change }, scope)).toThrow();
});
it.each([
  { cardId: id(90) }, { organizationId: id(90) }, { id: '00000000-0000-0000-0000-000000000000' }, { title: ' ' }, { title: 'x'.repeat(161) },
  { rank: rank(0) }, { rank: '9'.repeat(30) }, { version: -1 }, { deletedAt: now },
  { updatedAt: '2026-10-03T01:00:00.123455Z' }, { createdAt: '2026-02-30T00:00:00Z' },
])('rejects malformed or foreign Checklist content (%j)', change => {
  const value = page(); Object.assign(value.items[0].checklist, change);
  expect(() => parseChecklistPage(value, scope)).toThrow();
});
it.each([
  { completed: true }, { completedAt: now }, { completedBy: id(9) }, { text: 'x'.repeat(2001) },
  { checklistId: id(90) }, { organizationId: id(90) }, { text: 'bad\0text' }, { deletedAt: now },
])('rejects malformed attribution, tombstones or foreign items (%j)', change => {
  const value = items(); Object.assign(value.items[0], change);
  expect(() => parseChecklistItemPage(value, scope, parent.id)).toThrow();
});
it.each([{ total: 0 }, { completed: 2 }, { percent: NaN }, { percent: 50 }, { total: -1 }, { completed: 0.5 }])('rejects invented or inconsistent progress (%j)', change => {
  const value = items(); Object.assign(value.summary, change);
  expect(() => parseChecklistItemPage(value, scope, parent.id)).toThrow();
});
it('rejects duplicates, rank collisions, reversed order, non-advancing pages and overlong collections', () => {
  const value = items(); value.summary.total = 2;
  value.items.push({ ...item, id: id(6), rank: rank(2) });
  expect(parseChecklistItemPage(value, scope, parent.id).items).toHaveLength(2);
  expect(() => parseChecklistItemPage(value, scope, parent.id, `${parent.id}/${item.rank}/${item.id}`)).toThrow();
  value.items.reverse(); expect(() => parseChecklistItemPage(value, scope, parent.id)).toThrow();
  value.items = [{ ...item }, { ...item }]; expect(() => parseChecklistItemPage(value, scope, parent.id)).toThrow();
  value.items[1].id = id(6); expect(() => parseChecklistItemPage(value, scope, parent.id)).toThrow();
  const long = { ...items(), items: Array.from({ length: 51 }, (_, n) => ({ ...item, id: id(n + 20), rank: rank(n + 1) })), summary: { ...progress(), total: 51 } };
  expect(() => parseChecklistItemPage(long, scope, parent.id)).toThrow();
});
