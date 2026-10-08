import { cardDates, cardDueState, formatCardDate } from './cardDates';
import type { WorkCard } from '../../api/workManagement';
const card: WorkCard = { id: 'card', title: 'Card', description: null, rank: 'rank', version: 1,
  startAt: null, dueAt: '2026-10-03T08:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: false };
const dates = () => cardDates(card);
it('classifies deadlines using viewing timezone and completion before calendar urgency', () => {
  const now = Date.parse('2026-10-02T10:00:00Z');
  expect(cardDueState(dates(), 'UTC', now)).toBe('DUE_SOON');
  expect(cardDueState(dates(), 'Pacific/Honolulu', now)).toBe('DUE_TODAY');
  expect(cardDueState(dates(), 'UTC', now - 86400000)).toBe('UPCOMING');
  expect(cardDueState(dates(), 'UTC', now + 86400000)).toBe('OVERDUE');
  expect(cardDueState({ ...dates(), dueComplete: true }, 'UTC', now + 86400000)).toBe('COMPLETE');
});
it('retains microseconds at the due boundary and supports date-only and start-only Cards', () => {
  const dateOnly = cardDates({ ...card, dueAt: '2026-10-02T23:59:59.999999Z', dueHasTime: false });
  expect(cardDueState(dateOnly, 'UTC', Date.parse('2026-10-02T23:59:59.999Z'))).toBe('DUE_TODAY');
  expect(cardDueState(dateOnly, 'UTC', Date.parse('2026-10-03T00:00:00Z'))).toBe('OVERDUE');
  expect(cardDueState(cardDates({ ...card, startAt: '2026-10-02T00:00:00Z', dueAt: null, dueHasTime: false }), 'UTC')).toBeNull();
  const { startAt: _start, dueAt: _due, dueTimezone: _zone, dueHasTime: _timed, dueComplete: _complete, ...legacy } = card;
  expect(cardDates(legacy).dueAt).toBeNull();
});
it.each([
  { dueAt: '2026-02-30T00:00:00Z' }, { dueAt: '2026-10-02T12:00:00+01:00' }, { dueAt: '2026-10-02' },
  { dueTimezone: 'Unknown/Place' }, { dueTimezone: null }, { startAt: '2026-10-04T00:00:00Z' },
  { dueAt: null, dueHasTime: false, dueComplete: true }, { dueHasTime: undefined },
])('rejects malformed dates instead of inventing deadline state (%j)', change => {
  expect(() => cardDates({ ...card, ...change })).toThrow();
});
it('formats instants in configured timezone and suppresses time for date-only values', () => {
  expect(formatCardDate('2026-10-03T08:00:00Z', false, 'en-US', 'Pacific/Honolulu')).toBe('Oct 2, 2026');
  expect(formatCardDate('2026-10-03T08:00:00Z', true, 'en-US', 'Pacific/Honolulu')).toContain('10:00');
  expect(() => cardDueState(dates(), 'Unknown/Place')).toThrow();
});

it('reuses timezone formatters across a normal dated Board while recomputing current deadline state', async () => {
  vi.resetModules(); const datesModule = await import('./cardDates');
  const constructors = vi.spyOn(Intl, 'DateTimeFormat');
  try {
    const now = Date.parse('2026-10-02T10:00:00Z');
    for (let index = 0; index < 50; index++) {
      const values = datesModule.cardDates({ ...card, id: `card-${index}` });
      expect(datesModule.cardDueState(values, 'UTC', now)).toBe('DUE_SOON');
    }
    expect(constructors.mock.calls.length).toBeLessThanOrEqual(3);
    constructors.mockClear();
    expect(datesModule.cardDueState(dates(), 'UTC', now + 86400000)).toBe('OVERDUE');
    expect(datesModule.cardDueState(dates(), 'UTC', now - 86400000)).toBe('UPCOMING');
    expect(constructors).not.toHaveBeenCalled();
    expect(datesModule.cardDueState(dates(), 'Pacific/Honolulu', now)).toBe('DUE_TODAY');
    expect(() => datesModule.cardDueState(dates(), 'Unknown/Place', now)).toThrow();
  } finally { constructors.mockRestore(); }
});

it('bounds formatter retention and keeps locale, timezone and time-display choices independent', async () => {
  vi.resetModules(); const datesModule = await import('./cardDates');
  const value = '2026-10-03T08:00:00Z';
  const first = datesModule.formatCardDate(value, false, 'en-US-x-fixture-0', 'UTC');
  const constructors = vi.spyOn(Intl, 'DateTimeFormat');
  try {
    for (let index = 1; index <= 80; index++) datesModule.formatCardDate(value, false, `en-US-x-fixture-${index}`, 'UTC');
    constructors.mockClear();
    expect(datesModule.formatCardDate(value, false, 'en-US-x-fixture-0', 'UTC')).toBe(first);
    expect(constructors).toHaveBeenCalled();
    expect(datesModule.formatCardDate(value, false, 'en-US', 'Pacific/Honolulu')).toBe('Oct 2, 2026');
    expect(datesModule.formatCardDate(value, true, 'en-US', 'Pacific/Honolulu')).toContain('10:00');
    expect(datesModule.formatCardDate(value, false, 'fr-CA', 'UTC')).toBe(new Intl.DateTimeFormat('fr-CA', { timeZone: 'UTC', dateStyle: 'medium' }).format(new Date(value)));
  } finally { constructors.mockRestore(); }
});
