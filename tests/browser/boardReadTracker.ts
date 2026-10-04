import type { Page, Request } from '@playwright/test';

// A request from the previous screen can finish after navigation. Count only
// reads that started on the intended page, not every matching late response.
export function trackBoardReads(page: Page, boardId: string, pagePath: string): () => number {
  const pending = new Set<Request>();
  let completed = 0;
  page.on('request', request => {
    if (new URL(page.url()).pathname === pagePath && request.method() === 'GET'
      && new URL(request.url()).pathname === `/boards/${boardId}`) pending.add(request);
  });
  page.on('response', response => {
    if (pending.delete(response.request()) && response.status() === 200) completed++;
  });
  page.on('requestfailed', request => { pending.delete(request); });
  return () => completed;
}

// Disposable Card fixtures use monotonically increasing revisions to wait for
// actual canvas admission after a committed command, including a lost reply.
export function trackCardVersion(page: Page, boardId: string, cardId: string, pagePath: string): () => number {
  const pending = new Set<Request>(); let version = 0;
  page.on('request', request => {
    if (new URL(page.url()).pathname === pagePath && request.method() === 'GET'
      && new URL(request.url()).pathname === `/boards/${boardId}`) pending.add(request);
  });
  page.on('response', async response => {
    if (!pending.delete(response.request()) || response.status() !== 200) return;
    try {
      const snapshot = await response.json();
      if (snapshot.board?.id !== boardId || !Array.isArray(snapshot.lists)) return;
      for (const column of snapshot.lists) for (const card of column.cards ?? []) {
        if (card.id === cardId && Number.isSafeInteger(card.version) && card.version > version) version = card.version;
      }
    } catch { /* An interrupted response cannot establish admission. */ }
  });
  page.on('requestfailed', request => { pending.delete(request); });
  return () => version;
}
