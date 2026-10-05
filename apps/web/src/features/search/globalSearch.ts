import { notificationUuid as uuid } from '../notifications/notificationInbox';
import { parseSearchInteraction, type SearchInteraction } from './searchInteraction';
export type SearchItem = { id: string; organizationId: string; boardId: string; title: string; boardName: string; listName: string;
  dueAt: string | null; dueComplete: boolean; labels: string[]; members: string[]; moreLabels: boolean; moreMembers: boolean };
export type SearchPage = { items: SearchItem[]; nextCursor: string | null; interaction: SearchInteraction };
function text(value: unknown, max = 160): value is string { return typeof value === 'string' && value.length <= max; }
export function parseSearchPage(value: unknown, actor: string, after?: string): SearchPage {
  const p = value as Record<string, unknown> | null;
  if (!p || !Array.isArray(p.items) || p.items.length > 50
    || p.nextCursor !== null && (!text(p.nextCursor, 8192) || !p.nextCursor.length || p.nextCursor === after)) throw new Error('Invalid search page');
  const interaction = parseSearchInteraction(p.interaction, actor);
  const ids = new Set<string>();
  const items = p.items.map((value: unknown): SearchItem => {
    const d = value as Record<string, unknown> | null; const c = d?.card as Record<string, unknown> | null;
    if (!d || d.sourceKind !== 'CARD' || !c || !uuid(c.id) || !uuid(c.organizationId) || !uuid(c.boardId) || !uuid(c.listId)
      || !text(c.title) || !Number.isSafeInteger(c.version) || (c.version as number) < 1 || !text(d.boardName) || !text(d.listName)
      || !['active', 'archived'].includes(c.lifecycleState as string) || typeof c.dueComplete !== 'boolean'
      || c.dueAt !== null && (!text(c.dueAt, 80) || !Number.isFinite(Date.parse(c.dueAt)))
      || typeof d.hasMoreLabels !== 'boolean' || typeof d.hasMoreMembers !== 'boolean'
      || !Array.isArray(d.labels) || d.labels.length > 50 || !Array.isArray(d.members) || d.members.length > 50)
      throw new Error('Invalid search document');
    const id = c.id.toLowerCase(); if (ids.has(id)) throw new Error('Duplicate search document'); ids.add(id);
    const labels = d.labels.map((v: unknown) => { const l = v as Record<string, unknown> | null;
      if (!l || !uuid(l.id) || !text(l.name)) throw new Error('Invalid search label'); return l.name; });
    const members = d.members.map((v: unknown) => { const m = v as Record<string, unknown> | null;
      if (!m || !uuid(m.userId) || !text(m.displayName)) throw new Error('Invalid search member'); return m.displayName; });
    return { id, organizationId: c.organizationId.toLowerCase(), boardId: c.boardId.toLowerCase(), title: c.title,
      boardName: d.boardName, listName: d.listName, dueAt: c.dueAt as string | null, dueComplete: c.dueComplete,
      labels, members, moreLabels: d.hasMoreLabels, moreMembers: d.hasMoreMembers };
  });
  return { items, nextCursor: p.nextCursor as string | null, interaction };
}
