import type { WorkCard } from '../../api/workManagement';
import { dateCommand, dateDraft, dateDraftDirty } from './cardDateDraft';
const card: WorkCard = { id: 'card', title: 'Card', description: null, rank: 'rank', version: 3,
  startAt: '2040-01-01T12:00:00.123456Z', dueAt: '2040-01-03T09:59:59.999999Z', dueTimezone: 'Pacific/Honolulu', dueHasTime: false, dueComplete: false };
it('edits date-only in stored context and retains exact untouched start precision', () => {
  const d = dateDraft(card); expect(d.fields.due).toBe('2040-01-02'); expect(dateDraftDirty(d)).toBe(false);
  const input = dateCommand({ ...d, fields: { ...d.fields, complete: true } });
  expect(input).toEqual({ startAt: card.startAt, dueAt: '2040-01-02', dueTimezone: 'Pacific/Honolulu', dueHasTime: false, dueComplete: true, version: 3 });
});
it('retains exact UTC timed precision when only completion changes', () => {
  const d = dateDraft({ ...card, dueHasTime: true });
  expect(dateCommand({ ...d, fields: { ...d.fields, complete: true } }).dueAt).toBe(card.dueAt);
});
it('supports UTC editing, nullable dates and canonical clearing', () => {
  const d = dateDraft(card);
  expect(dateCommand({ ...d, fields: { ...d.fields, start: '2040-01-01T14:00', due: '2040-01-02T16:00:00.000', timed: true } }).startAt).toBe('2040-01-01T14:00:00.000Z');
  expect(dateCommand({ ...d, fields: { ...d.fields, start: '', due: '', complete: false, timed: false } })).toEqual({ startAt: null, dueAt: null, dueTimezone: null, dueHasTime: false, dueComplete: false, version: 3 });
});
it.each([
  { due: '2040-02-30' }, { due: 'not-a-date' }, { start: '2040-02-30T12:00' }, { zone: 'Unknown/Place' },
  { due: '', complete: true }, { due: '', timed: true }, { start: '2040-01-04T12:00', due: '2040-01-03T12:00', timed: true },
])('rejects invalid date drafts (%j)', change => {
  const d = dateDraft(card); expect(() => dateCommand({ ...d, fields: { ...d.fields, ...change } })).toThrow();
});
