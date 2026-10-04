import { validateBoardStarSync } from './boardStarSync';
export type StarHistoryItem = { eventId: string; entityId: string; version: number; createdAt: string };
export type StarHistoryPage = { items: StarHistoryItem[]; nextAfter: number | null };
export function parseStarHistory(input: unknown, organizationId: string, boardId: string, userId: string,
  after = 0, entityId?: string): StarHistoryPage {
  const p = input as Record<string, unknown> | null;
  if (!p || Object.keys(p).sort().join(',') !== 'boardId,items,nextAfter,organizationId,userId'
    || !Array.isArray(p.items) || p.nextAfter !== null && (!Number.isSafeInteger(p.nextAfter) || (p.nextAfter as number) <= after)) throw new Error('Invalid private history');
  const items = p.items as StarHistoryItem[];
  const end = items.at(-1)?.version ?? after;
  if (p.nextAfter !== null && (items.length !== 50 || p.nextAfter !== end)
    || entityId && items.some(item => item.entityId !== entityId)) throw new Error('Invalid private history');
  const admitted = validateBoardStarSync({ organizationId: p.organizationId, boardId: p.boardId, userId: p.userId,
    cursor: String(end), events: items, hasMore: p.nextAfter !== null, resetRequired: false },
  organizationId, boardId, userId, String(after), new Map());
  if (!admitted) throw new Error('Invalid private history');
  return { items, nextAfter: p.nextAfter as number | null };
}
