import type { Page, Request } from '@playwright/test';

// Passive readiness: observe a real scoped stream head and the protected read
// started after it. A previous screen/read cannot admit a keyboard action.
export function trackInvitationAdmission(page: Page, organization: string, actor: string, board: string | undefined, pagePath: string, protectedReadPath?: string) {
  const socketPath = board ? '/boards/live' : '/organizations/live/metadata';
  const scope = board ?? organization;
  const readPath = protectedReadPath ?? (board ? `/boards/${board}` : `/organizations/${organization}/members/${actor}`);
  let heads = 0, readHead = 0, reads = 0;
  const pending = new Map<Request, number>();
  page.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== socketPath) return;
    const watches = new Set<string>(), observed = new Set<string>();
    socket.on('framesent', frame => {
      if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part);
          if (value.type === 4 && value.target === 'Watch' && value.arguments?.[0] === scope && typeof value.invocationId === 'string')
            watches.add(value.invocationId);
        } catch { /* Ignore handshake/non-JSON framing; do not change the socket. */ }
      }
    });
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part);
          const item = board ? value.item : value.item?.organizationId === organization && value.item?.userId === actor ? value.item.page : undefined;
          if (value.type === 2 && watches.has(value.invocationId) && !observed.has(value.invocationId)
            && item && typeof item.cursor === 'string' && typeof item.resetRequired === 'boolean' && Array.isArray(item.events)) {
            observed.add(value.invocationId); heads++;
          }
        } catch { /* Passive observation only. */ }
      }
    });
  });
  page.on('request', request => {
    if (heads > 0 && new URL(page.url()).pathname === pagePath && request.method() === 'GET'
      && new URL(request.url()).pathname === readPath) pending.set(request, heads);
  });
  page.on('response', response => {
    const head = pending.get(response.request()); pending.delete(response.request());
    if (head !== undefined && head === heads && response.status() === 200) { readHead = head; reads++; }
  });
  page.on('requestfailed', request => pending.delete(request));
  return { heads: () => heads, reads: () => reads, ready: () => heads > 0 && readHead === heads };
}
