import type { WorkCard } from '../../api/workManagement';

export type CardDates = { startAt: string | null; dueAt: string | null; dueTimezone: string | null; dueHasTime: boolean; dueComplete: boolean };
export type DueState = 'UPCOMING' | 'DUE_SOON' | 'DUE_TODAY' | 'OVERDUE' | 'COMPLETE';
const utc = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/;
// Cache only Intl configuration, never Card values, profiles or deadline state.
// Bound retention even when locale/timezone preferences change repeatedly.
const formatters = new Map<string, Intl.DateTimeFormat>();
function formatter(locale: string, zone: string, kind: 'zone' | 'day' | 'clock' | 'date' | 'datetime'): Intl.DateTimeFormat {
  const key = JSON.stringify([locale, zone, kind]);
  const retained = formatters.get(key);
  if (retained) { formatters.delete(key); formatters.set(key, retained); return retained; }
  const options: Intl.DateTimeFormatOptions = kind === 'day' ? { year: 'numeric', month: '2-digit', day: '2-digit' }
    : kind === 'clock' ? { hourCycle: 'h23', hour: '2-digit', minute: '2-digit', second: '2-digit' }
    : kind === 'zone' ? {} : { dateStyle: 'medium', ...(kind === 'datetime' ? { timeStyle: 'short' as const } : {}) };
  const created = new Intl.DateTimeFormat(locale, { timeZone: zone, ...options });
  if (formatters.size >= 64) formatters.delete(formatters.keys().next().value!);
  formatters.set(key, created); return created;
}
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
  formatter('en', zone, 'zone').format(0);
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
  const parts = formatter('en', dateTimezone(zone), 'day').formatToParts(instant);
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
// Wake at due/24-hour boundaries and local midnight. A bounded heartbeat also
// catches wall-clock changes and DST without constructing a guessed UTC day.
export function nextCardDateWake(cards: Pick<WorkCard, 'dueAt' | 'dueComplete'>[], timezone: string, now: number): number {
  let delay = 30_000;
  for (const card of cards) {
    if (!card.dueAt || card.dueComplete) continue;
    let ticks: bigint;
    try { ticks = instant(card.dueAt); } catch { continue; }
    const milliseconds = ticks / 10000n, remainder = ticks % 10000n;
    const firstAfter = Number(milliseconds + (remainder < 0n ? 0n : 1n));
    const atOrAfter = Number(milliseconds + (remainder > 0n ? 1n : 0n));
    for (const boundary of [firstAfter, atOrAfter - 86_400_000])
      if (boundary > now) delay = Math.min(delay, boundary - now);
  }
  const parts = formatter('en', timezone, 'clock').formatToParts(now);
  const part = (name: string) => Number(parts.find(value => value.type === name)!.value);
  const midnight = (86_400 - part('hour') * 3600 - part('minute') * 60 - part('second')) * 1000 - now % 1000;
  return Math.max(1, Math.min(delay, midnight));
}
export function formatCardDate(value: string, timed: boolean, locale: string, zone: string): string {
  instant(value);
  return formatter(locale, dateTimezone(zone), timed ? 'datetime' : 'date').format(new Date(value));
}
