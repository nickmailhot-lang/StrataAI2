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
