import type { WorkCard } from '../../api/workManagement';

export type CardDates = { startAt: string | null; dueAt: string | null; dueTimezone: string | null; dueHasTime: boolean; dueComplete: boolean };
export type DueState = 'UPCOMING' | 'DUE_SOON' | 'DUE_TODAY' | 'OVERDUE' | 'COMPLETE';
const utc = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/;
function instant(text: string): bigint {
  const match = utc.exec(text);
  if (!match) throw new Error('Invalid date instant');
  const milliseconds = Date.parse(`${match[1]}Z`);
  if (!Number.isFinite(milliseconds) || new Date(milliseconds).toISOString().slice(0, 19) !== match[1]) throw new Error('Invalid date instant');
  return BigInt(milliseconds) * 10000n + BigInt((match[2] ?? '').padEnd(7, '0'));
}
export function sameDateInstant(left: string, right: string): boolean { return instant(left) === instant(right); }
export function dateInstantTicks(value: string): bigint { return instant(value); }
export function dateTimezone(zone: string): string {
  if (!zone || zone.length > 100 || zone.trim() !== zone) throw new Error('Invalid date timezone');
  new Intl.DateTimeFormat('en', { timeZone: zone }).format(0);
  return zone;
}
export function cardDates(card: WorkCard): CardDates {
  const { startAt, dueAt, dueTimezone, dueHasTime, dueComplete } = card;
  // Older seeded Cards predate the date feature and have no date fields at all.
  if ([startAt, dueAt, dueTimezone, dueHasTime, dueComplete].every(value => value === undefined))
    return { startAt: null, dueAt: null, dueTimezone: null, dueHasTime: false, dueComplete: false };
  if (startAt !== null && typeof startAt !== 'string' || dueAt !== null && typeof dueAt !== 'string' ||
      dueTimezone !== null && typeof dueTimezone !== 'string' || typeof dueHasTime !== 'boolean' || typeof dueComplete !== 'boolean') throw new Error('Invalid Card dates');
  const start = startAt === null ? null : instant(startAt), due = dueAt === null ? null : instant(dueAt);
  if (start !== null && due !== null && start > due || due === null && (dueHasTime || dueComplete)) throw new Error('Invalid Card date order or flags');
  if (start !== null || due !== null) {
    if (dueTimezone === null) throw new Error('Missing date timezone');
    dateTimezone(dueTimezone);
  } else if (dueTimezone !== null) throw new Error('Unexpected date timezone');
  return { startAt, dueAt, dueTimezone, dueHasTime, dueComplete };
}
function day(instant: Date, zone: string): string {
  const parts = new Intl.DateTimeFormat('en', { timeZone: dateTimezone(zone), year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(instant);
  return ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)!.value).join('-');
}
export function cardDueState(dates: CardDates, viewingTimezone: string, now = Date.now()): DueState | null {
  if (!Number.isSafeInteger(now)) throw new Error('Invalid current date');
  dateTimezone(viewingTimezone);
  if (dates.dueAt === null) return null;
  const due = instant(dates.dueAt), current = BigInt(now) * 10000n;
  if (dates.dueComplete) return 'COMPLETE';
  if (current > due) return 'OVERDUE';
  if (day(new Date(dates.dueAt), viewingTimezone) === day(new Date(now), viewingTimezone)) return 'DUE_TODAY';
  if (due - current <= 24n * 60n * 60n * 10000000n) return 'DUE_SOON';
  return 'UPCOMING';
}
export function formatCardDate(value: string, timed: boolean, locale: string, zone: string): string {
  instant(value);
  return new Intl.DateTimeFormat(locale, { timeZone: dateTimezone(zone), dateStyle: 'medium', ...(timed ? { timeStyle: 'short' as const } : {}) }).format(new Date(value));
}
