import type { WorkCard } from '../../api/workManagement';
import { cardDates, dateTimezone } from './cardDates';

export type DateFields = { start: string; due: string; zone: string; timed: boolean; complete: boolean };
export type DateDraft = { base: WorkCard; original: DateFields; fields: DateFields };
export type DateCommand = { startAt: string | null; dueAt: string | null; dueTimezone: string | null; dueHasTime: boolean; dueComplete: boolean; version: number };
export function localDate(value: string, zone: string): string {
  const parts = new Intl.DateTimeFormat('en', { timeZone: dateTimezone(zone), year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date(value));
  return ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)!.value).join('-');
}
function field(value: string | null): string { return value ? new Date(value).toISOString().slice(0, 23) : ''; }
export function dateDraft(card: WorkCard, fallbackZone = 'UTC'): DateDraft {
  const dates = cardDates(card);
  const fields: DateFields = { start: field(dates.startAt), due: dates.dueAt ? dates.dueHasTime ? field(dates.dueAt) : localDate(dates.dueAt, dates.dueTimezone!) : '',
    zone: dates.dueTimezone ?? dateTimezone(fallbackZone), timed: dates.dueHasTime, complete: dates.dueComplete };
  return { base: card, original: fields, fields };
}
export function dateDraftDirty(draft: DateDraft): boolean { return JSON.stringify(draft.fields) !== JSON.stringify(draft.original); }
function utcInput(value: string): string {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,3})?)?$/.test(value)) throw new Error('Check the UTC date and time.');
  const full = value.length === 16 ? `${value}:00` : value;
  const parsed = new Date(`${full}Z`);
  if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 19) !== full.slice(0, 19)) throw new Error('Check the UTC date and time.');
  return parsed.toISOString();
}
function calendar(value: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) !== value) throw new Error('Check the due date.');
  return value;
}
export function dateCommand(draft: DateDraft): DateCommand {
  const f = draft.fields, before = cardDates(draft.base);
  if (!f.due && (f.timed || f.complete)) throw new Error('Set a due date before choosing time or completion.');
  const start = f.start ? f.start === draft.original.start ? before.startAt : utcInput(f.start) : null;
  const due = f.due ? f.timed ? f.due === draft.original.due && draft.original.timed ? before.dueAt : utcInput(f.due) : calendar(f.due) : null;
  const zone = start || due ? dateTimezone(f.zone) : null;
  if (start && due && f.timed && Date.parse(start) > Date.parse(due)) throw new Error('Start must not follow due.');
  return { startAt: start, dueAt: due, dueTimezone: zone, dueHasTime: f.timed, dueComplete: f.complete, version: draft.base.version };
}
