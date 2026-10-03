import { notificationUuid } from '../notifications/notificationInbox';
import { dateInstantTicks } from './cardDates';

export type ChecklistScope = { organizationId: string; boardId: string; cardId: string };
export type Checklist = { id: string; organizationId: string; cardId: string; title: string; rank: string;
  createdAt: string; updatedAt: string; version: number; deletedAt: string | null };
export type ChecklistItem = { id: string; organizationId: string; checklistId: string; text: string; rank: string;
  completed: boolean; completedAt: string | null; completedBy: string | null;
  createdAt: string; updatedAt: string; version: number; deletedAt: string | null };
export type ChecklistSummary = { checklist: Checklist; completed: number; total: number; percent: number };
export type ChecklistPage = ChecklistScope & { cardVersion: number; canEdit: boolean; items: ChecklistSummary[]; nextCursor: string | null };
export type ChecklistItemPage = ChecklistScope & { cardVersion: number; canEdit: boolean; summary: ChecklistSummary; items: ChecklistItem[]; nextCursor: string | null };
export type ChecklistChange = ChecklistScope & { cardVersion: number; checklist: Checklist; changed: boolean };

const invalid = () => new Error('Invalid checklist response');
const sameId = (value: unknown, expected: string) => notificationUuid(value) && notificationUuid(expected) && value.toLowerCase() === expected.toLowerCase();
const count = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) >= 0;
const version = (value: unknown): value is number => count(value) && value > 0;
const rank = (value: unknown): value is string => typeof value === 'string' && /^[0-9]{30}$/.test(value) && value > '0'.repeat(30) && value < '9'.repeat(30);
const text = (value: unknown, limit: number): value is string => typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= limit && !value.includes('\0');
function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  return value as Record<string, unknown>;
}
function instant(value: unknown): bigint {
  if (typeof value !== 'string') throw invalid();
  return dateInstantTicks(value);
}
function lifecycle(row: Record<string, unknown>) {
  if (!notificationUuid(row.id) || !rank(row.rank) || !version(row.version) || row.deletedAt !== null ||
    instant(row.updatedAt) < instant(row.createdAt)) throw invalid();
}
function checklist(value: unknown, scope: ChecklistScope): Checklist {
  const row = record(value); lifecycle(row);
  if (!sameId(row.organizationId, scope.organizationId) || !sameId(row.cardId, scope.cardId) || !text(row.title, 160)) throw invalid();
  return row as Checklist;
}
function summary(value: unknown, scope: ChecklistScope): ChecklistSummary {
  const row = record(value); checklist(row.checklist, scope);
  if (!count(row.total) || !count(row.completed) || row.completed > row.total || typeof row.percent !== 'number' || !Number.isFinite(row.percent) ||
    Math.abs(row.percent - (row.total === 0 ? 0 : row.completed * 100 / row.total)) > 1e-10) throw invalid();
  return row as ChecklistSummary;
}
function item(value: unknown, scope: ChecklistScope, parent: string): ChecklistItem {
  const row = record(value); lifecycle(row);
  if (!sameId(row.organizationId, scope.organizationId) || !sameId(row.checklistId, parent) || !text(row.text, 2000) || typeof row.completed !== 'boolean') throw invalid();
  if (row.completed) {
    const completed = instant(row.completedAt);
    if (!notificationUuid(row.completedBy) || completed < instant(row.createdAt) || completed > instant(row.updatedAt)) throw invalid();
  } else if (row.completedAt !== null || row.completedBy !== null) throw invalid();
  return row as ChecklistItem;
}
function page(value: unknown, scope: ChecklistScope) {
  const row = record(value);
  if (!sameId(row.organizationId, scope.organizationId) || !sameId(row.boardId, scope.boardId) || !sameId(row.cardId, scope.cardId) ||
    !version(row.cardVersion) || typeof row.canEdit !== 'boolean' || !Array.isArray(row.items) || row.items.length > 50) throw invalid();
  return row;
}
function cursor(value: unknown, parent: string) {
  if (typeof value !== 'string') throw invalid();
  const parts = value.split('/');
  if (parts.length !== 3 || !sameId(parts[0], parent) || !rank(parts[1]) || !notificationUuid(parts[2])) throw invalid();
  return { rank: parts[1], id: parts[2].toLowerCase() };
}
function order(left: { rank: string; id: string }, right: { rank: string; id: string }) {
  return left.rank < right.rank || left.rank === right.rank && left.id.toLowerCase() < right.id.toLowerCase();
}
function ordered(rows: { rank: string; id: string }[], parent: string, next: unknown, after?: string) {
  let previous = after === undefined ? undefined : cursor(after, parent);
  const seen = new Set<string>(); const ranks = new Set<string>();
  for (const row of rows) {
    if (seen.has(row.id.toLowerCase()) || ranks.has(row.rank) || previous && !order(previous, row)) throw invalid();
    seen.add(row.id.toLowerCase()); ranks.add(row.rank); previous = row;
  }
  if (next !== null) {
    const parsed = cursor(next, parent); const last = rows.at(-1);
    if (rows.length !== 50 || !last || parsed.rank !== last.rank || !sameId(parsed.id, last.id)) throw invalid();
  }
}
export function parseChecklistPage(value: unknown, scope: ChecklistScope, after?: string): ChecklistPage {
  const row = page(value, scope);
  const rows = (row.items as unknown[]).map(value => summary(value, scope));
  ordered(rows.map(value => value.checklist), scope.cardId, row.nextCursor, after);
  return row as ChecklistPage;
}
export function parseChecklistItemPage(value: unknown, scope: ChecklistScope, checklistId: string, after?: string): ChecklistItemPage {
  const row = page(value, scope); const progress = summary(row.summary, scope);
  if (!sameId(progress.checklist.id, checklistId)) throw invalid();
  const rows = (row.items as unknown[]).map(value => item(value, scope, checklistId));
  const completed = rows.filter(value => value.completed).length;
  if (progress.total < rows.length || progress.completed < completed || progress.total - progress.completed < rows.length - completed) throw invalid();
  ordered(rows, checklistId, row.nextCursor, after);
  return row as ChecklistItemPage;
}
export function parseChecklistCreated(value: unknown, scope: ChecklistScope, title: string, cardVersion: number): ChecklistChange {
  const row = record(value); const child = checklist(row.checklist, scope);
  if (!sameId(row.organizationId, scope.organizationId) || !sameId(row.boardId, scope.boardId) || !sameId(row.cardId, scope.cardId) ||
    !version(cardVersion) || !version(row.cardVersion) || row.cardVersion !== cardVersion + 1 || row.changed !== true ||
    child.title !== title || child.version !== 1 || instant(child.createdAt) !== instant(child.updatedAt)) throw invalid();
  return row as ChecklistChange;
}
