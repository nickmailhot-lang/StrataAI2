import type { Page, Request } from '@playwright/test';

// Passive readiness: observe a real scoped stream head and the protected read
// started after it. A previous screen/read cannot admit a keyboard action.
// Optional issuance admission observes new delivery, not historical replay.
// Organization metadata names the invitation; Board invalidations deliberately
// name only the Board. The caller can bind the acknowledgment after its frame.
export function trackInvitationAdmission(page: Page, organization: string, actor: string, board: string | undefined, pagePath: string, protectedReadPath?: string, issuedInvitation?: { id?: string }) {
  const socketPath = board ? '/boards/live' : '/organizations/live/metadata';
  const scope = board ?? organization;
  const readPath = protectedReadPath ?? (board ? `/boards/${board}` : `/organizations/${organization}/members/${actor}`);
  let heads = 0, generation = 0, readGeneration = -1, reads = 0;
  let documentGeneration = 0;
  type Stream = { generation: number; readGeneration: number };
  const streams = new Set<Stream>();
  const issued = new Set<string>();
  const seen = new Set<string>();
  const pending = new Map<Request, { generation: number; document: number; streams: [Stream, number][] }>();
  page.on('framenavigated', frame => {
    if (frame !== page.mainFrame()) return;
    documentGeneration++; generation++; readGeneration = -1;
    streams.clear(); pending.clear();
  });
  page.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== socketPath) return;
    const document = documentGeneration;
    const stream: Stream = { generation: 0, readGeneration: -1 };
    let closed = false;
    const watches = new Set<string>(), observed = new Set<string>();
    socket.on('close', () => { closed = true; streams.delete(stream); });
    socket.on('framesent', frame => {
      if (closed || document !== documentGeneration || typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part);
          if (value.type === 4 && value.target === 'Watch' && value.arguments?.[0] === scope && typeof value.invocationId === 'string')
            watches.add(value.invocationId);
        } catch { /* Ignore handshake/non-JSON framing; do not change the socket. */ }
      }
    });
    socket.on('framereceived', frame => {
      if (closed || document !== documentGeneration || typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part);
          const item = board ? value.item : value.item?.organizationId === organization && value.item?.userId === actor ? value.item.page : undefined;
          if (value.type !== 2 || !watches.has(value.invocationId) || !item || typeof item.cursor !== 'string'
            || typeof item.resetRequired !== 'boolean' || !Array.isArray(item.events)) continue;
          if (issuedInvitation && (typeof item.pending !== 'boolean' || item.events.some((event: {
            eventId?: unknown; eventType?: unknown; organizationId?: unknown; boardId?: unknown; entityId?: unknown;
          }) => !event || typeof event.eventId !== 'string' || typeof event.eventType !== 'string'
            || event.organizationId !== organization || event.boardId !== (board ?? null) || typeof event.entityId !== 'string'))) continue;
          if (!observed.has(value.invocationId)) {
            observed.add(value.invocationId); heads++; generation++;
            stream.generation++; stream.readGeneration = -1; streams.add(stream);
          }
          if (issuedInvitation) {
            if (item.resetRequired) { generation++; seen.clear(); }
            for (const event of item.events) {
              if (!seen.has(event.eventId)) { seen.add(event.eventId); generation++; }
              if (event.entityType === (board ? 'Board' : 'Invitation') && (!board || event.entityId === board)
                && event.eventType === (board ? 'BOARD_MEMBER_INVITED' : 'ORGANIZATION_MEMBER_INVITED')) issued.add(event.entityId);
            }
          }
        } catch { /* Passive observation only. */ }
      }
    });
  });
  page.on('request', request => {
    if (streams.size > 0 && new URL(page.url()).pathname === pagePath && request.method() === 'GET'
      && new URL(request.url()).pathname === readPath) pending.set(request, {
      generation, document: documentGeneration, streams: [...streams].map(stream => [stream, stream.generation]),
    });
  });
  page.on('response', response => {
    const started = pending.get(response.request()); pending.delete(response.request());
    if (!started || started.document !== documentGeneration || response.status() !== 200) return;
    let currentStreamRead = false;
    for (const [stream, version] of started.streams) {
      if (streams.has(stream) && stream.generation === version) {
        stream.readGeneration = version; currentStreamRead = true;
      }
    }
    if (started.generation === generation) readGeneration = generation;
    if (issuedInvitation ? started.generation === generation : currentStreamRead) reads++;
  });
  page.on('requestfailed', request => pending.delete(request));
  return { heads: () => heads, reads: () => reads,
    // The caption and invitation form own independent connections. A new head
    // fences its own earlier read, not another live connection's proof. Issued
    // source/event admission keeps its existing aggregate generation fence.
    ready: () => streams.size > 0 && (issuedInvitation
      ? readGeneration === generation && !!issuedInvitation.id && issued.has(board ?? issuedInvitation.id)
      : [...streams].some(stream => stream.readGeneration === stream.generation)) };
}

// A displayed review can precede the Board event that invalidates it. Observe
// that actual source and the configured protected read started after its frame.
export function trackBoardHistoryChanges(page: Page, organization: string, board: string, pagePath: string,
  protectedReadPath = `/boards/${board}/invitations`) {
  let epoch = 0, readEpoch = -1;
  const seen = new Set<string>(), counts = new Map<string, number>(), pending = new Map<Request, number>();
  page.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== '/boards/live') return;
    const watches = new Set<string>(), heads = new Set<string>();
    socket.on('framesent', frame => {
      if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part);
          if (value.type === 4 && value.target === 'Watch' && value.arguments?.[0] === board && typeof value.invocationId === 'string') watches.add(value.invocationId);
        } catch { /* Passive observation only. */ }
      }
    });
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part), item = value.item;
          if (value.type !== 2 || !watches.has(value.invocationId) || !item || typeof item.cursor !== 'string'
            || typeof item.pending !== 'boolean' || typeof item.resetRequired !== 'boolean' || !Array.isArray(item.events)
            || item.events.some((event: { eventId?: unknown; eventType?: unknown; organizationId?: unknown; boardId?: unknown }) =>
              typeof event.eventId !== 'string' || typeof event.eventType !== 'string' || event.organizationId !== organization || event.boardId !== board)) continue;
          let changed = !heads.has(value.invocationId); heads.add(value.invocationId);
          for (const event of item.events) if (!seen.has(event.eventId)) {
            seen.add(event.eventId); counts.set(event.eventType, (counts.get(event.eventType) ?? 0) + 1); changed = true;
          }
          if (changed) epoch++;
        } catch { /* Never change or emit private frames. */ }
      }
    });
  });
  page.on('request', request => {
    if (epoch > 0 && new URL(page.url()).pathname === pagePath && request.method() === 'GET'
      && new URL(request.url()).pathname === protectedReadPath) pending.set(request, epoch);
  });
  page.on('response', response => {
    const started = pending.get(response.request()); pending.delete(response.request());
    if (started !== undefined && started === epoch && response.status() === 200) readEpoch = started;
  });
  page.on('requestfailed', request => pending.delete(request));
  return { count: (type: string) => counts.get(type) ?? 0,
    ready: () => epoch > 0 && readEpoch === epoch,
    settled: (type: string, minimum: number) => epoch > 0 && readEpoch === epoch && (counts.get(type) ?? 0) >= minimum };
}

// Archive consent/retry must follow the actual Organization directory source,
// rather than an empty directory read that happened before its invalidation.
export function trackArchivedBoardChanges(page: Page, organization: string, actor: string, board: string, pagePath: string) {
  let epoch = 0, readEpoch = -1;
  const seen = new Set<string>(), counts = new Map<string, number>(), pending = new Map<Request, number>();
  page.on('websocket', socket => {
    if (new URL(socket.url()).pathname !== '/organizations/live') return;
    const watches = new Set<string>(), heads = new Set<string>();
    socket.on('framesent', frame => {
      if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part);
          if (value.type === 4 && value.target === 'Watch' && value.arguments?.[0] === organization && typeof value.invocationId === 'string') watches.add(value.invocationId);
        } catch { /* Passive observation only. */ }
      }
    });
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== pagePath) return;
      for (const part of frame.payload.split('\u001e').filter(Boolean)) {
        try {
          const value = JSON.parse(part), envelope = value.item, item = envelope?.page;
          if (value.type !== 2 || !watches.has(value.invocationId) || envelope.organizationId !== organization || envelope.userId !== actor
            || !item || typeof item.cursor !== 'string' || typeof item.pending !== 'boolean' || typeof item.resetRequired !== 'boolean'
            || !Array.isArray(item.events) || item.events.some((event: { eventId?: unknown; eventType?: unknown; boardId?: unknown; version?: unknown }) =>
              typeof event.eventId !== 'string' || typeof event.eventType !== 'string' || typeof event.boardId !== 'string' || !Number.isSafeInteger(event.version))) continue;
          let changed = !heads.has(value.invocationId); heads.add(value.invocationId);
          for (const event of item.events) if (!seen.has(event.eventId)) {
            seen.add(event.eventId); changed = true;
            if (event.boardId === board) counts.set(event.eventType, (counts.get(event.eventType) ?? 0) + 1);
          }
          if (changed) epoch++;
        } catch { /* Never alter or emit private frames. */ }
      }
    });
  });
  page.on('request', request => {
    if (epoch > 0 && new URL(page.url()).pathname === pagePath && request.method() === 'GET'
      && new URL(request.url()).pathname === `/organizations/${organization}/archived-boards`) pending.set(request, epoch);
  });
  page.on('response', response => {
    const started = pending.get(response.request()); pending.delete(response.request());
    if (started !== undefined && started === epoch && response.status() === 200) readEpoch = started;
  });
  page.on('requestfailed', request => pending.delete(request));
  return { count: (type: string) => counts.get(type) ?? 0, ready: () => epoch > 0 && readEpoch === epoch,
    settled: (type: string, minimum: number) => epoch > 0 && readEpoch === epoch && (counts.get(type) ?? 0) >= minimum };
}
