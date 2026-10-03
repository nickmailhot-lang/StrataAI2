import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';

export const reminderIntervals = { AT_DUE: 'At the due time', '5_MINUTES': '5 minutes before', '1_HOUR': '1 hour before', '1_DAY': '1 day before' } as const;
export type ReminderInterval = keyof typeof reminderIntervals;
const durations: Record<ReminderInterval, bigint> = { AT_DUE: 0n, '5_MINUTES': 3000000000n, '1_HOUR': 36000000000n, '1_DAY': 864000000000n };
export type PersonalReminder = { id: string; organizationId: string; userId: string; cardId: string; intervalCode: ReminderInterval;
  enabled: boolean; dueAt: string | null; triggerAt: string | null; status: 'SCHEDULED' | 'SUSPENDED' | 'CANCELLED' | 'FIRED';
  generation: number; version: number; createdAt: string; updatedAt: string };
export type ReminderState = { organizationId: string; boardId: string; cardId: string; userId: string; cardVersion: number;
  reminder: PersonalReminder | null; options: { code: ReminderInterval; label: string; triggerAt: string }[]; canChange: boolean; changed: boolean };
type Scope = { organizationId: string; boardId: string; cardId: string; userId: string; dueAt: string | null };
const code = (value: unknown): value is ReminderInterval => typeof value === 'string' && Object.hasOwn(reminderIntervals, value);
const positive = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
function sameId(left: unknown, right: string) { return notificationUuid(left) && left.toLowerCase() === right.toLowerCase(); }
function time(value: unknown): string { if (typeof value !== 'string') throw new Error('Invalid Reminder time'); dateInstantTicks(value); return value; }

// Reject a cross-person/scope response before rendering a personal choice.
export function parseReminderState(value: unknown, scope: Scope): ReminderState {
  const state = value as ReminderState | null;
  if (!state || !sameId(state.organizationId, scope.organizationId) || !sameId(state.boardId, scope.boardId) ||
    !sameId(state.cardId, scope.cardId) || !sameId(state.userId, scope.userId) || !positive(state.cardVersion) ||
    typeof state.canChange !== 'boolean' || typeof state.changed !== 'boolean' || !Array.isArray(state.options) || state.options.length > 4 ||
    state.reminder === undefined || !state.canChange && state.options.length > 0) throw new Error('Invalid Reminder scope');
  const seen = new Set<string>();
  for (const option of state.options) {
    if (!option || !code(option.code) || seen.has(option.code) || option.label !== reminderIntervals[option.code] || scope.dueAt === null ||
      dateInstantTicks(time(option.triggerAt)) !== dateInstantTicks(scope.dueAt) - durations[option.code]) throw new Error('Invalid Reminder option');
    seen.add(option.code);
  }
  const row = state.reminder;
  if (row !== null) {
    if (!row || !notificationUuid(row.id) || !sameId(row.organizationId, scope.organizationId) || !sameId(row.cardId, scope.cardId) ||
      !sameId(row.userId, scope.userId) || !code(row.intervalCode) || typeof row.enabled !== 'boolean' || !positive(row.generation) ||
      !positive(row.version) || row.version < row.generation || !['SCHEDULED', 'SUSPENDED', 'CANCELLED', 'FIRED'].includes(row.status) ||
      dateInstantTicks(time(row.updatedAt)) < dateInstantTicks(time(row.createdAt))) throw new Error('Invalid personal Reminder');
    if (row.dueAt !== null) time(row.dueAt);
    if (row.triggerAt !== null) time(row.triggerAt);
    if (row.status === 'CANCELLED') {
      if (row.enabled || row.dueAt !== null || row.triggerAt !== null) throw new Error('Invalid cancelled Reminder');
    } else {
      if (!row.enabled || (row.dueAt === null) !== (scope.dueAt === null) || row.dueAt !== null &&
        dateInstantTicks(row.dueAt) !== dateInstantTicks(scope.dueAt!)) throw new Error('Invalid Reminder due time');
      if (row.status === 'SUSPENDED') { if (row.triggerAt !== null) throw new Error('Invalid suspended Reminder'); }
      else if (row.dueAt === null || row.triggerAt === null || dateInstantTicks(row.triggerAt) !== dateInstantTicks(row.dueAt) - durations[row.intervalCode])
        throw new Error('Invalid scheduled Reminder');
    }
  }
  return state;
}
