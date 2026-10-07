import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';

export type InvitationRecipientSync = {
  cursor: string; resetRequired: boolean; lastSequence: bigint | undefined; eventIds: string[];
};

// The protected cursor is opaque. Sequence continuity can only be checked from
// the first actual event after a reset; it must never be inferred from the token.
export function validateInvitationRecipientSync(input: unknown, cursor: string | undefined,
  lastSequence: bigint | undefined, seen: ReadonlySet<string>): InvitationRecipientSync | null {
  try {
    const page = input as Record<string, unknown> | null;
    if (!page || Array.isArray(page) || Object.keys(page).sort().join(',') !== 'cursor,events,hasMore,resetRequired'
      || typeof page.cursor !== 'string' || !/^[A-Za-z0-9_-]{1,4096}$/.test(page.cursor)
      || typeof page.hasMore !== 'boolean' || typeof page.resetRequired !== 'boolean'
      || !Array.isArray(page.events) || page.events.length > 50) return null;
    if (page.resetRequired) {
      if (page.hasMore || page.events.length) return null;
      return { cursor: page.cursor, resetRequired: true, lastSequence: undefined, eventIds: [] };
    }
    if (cursor === undefined || page.hasMore && page.events.length !== 50) return null;
    const eventIds: string[] = []; const local = new Set<string>(); let sequence = lastSequence;
    for (const value of page.events) {
      const event = value as Record<string, unknown> | null;
      if (!event || Array.isArray(event) || Object.keys(event).sort().join(',') !== 'createdAt,eventId,eventType,sequence'
        || !notificationUuid(event.eventId) || seen.has(event.eventId.toLowerCase()) || local.has(event.eventId.toLowerCase())
        || !['INVITATION_CREATED', 'INVITATION_ACCEPTED', 'INVITATION_REVOKED'].includes(event.eventType as string)
        || typeof event.sequence !== 'string' || !/^[1-9][0-9]{0,18}$/.test(event.sequence)
        || BigInt(event.sequence) > 9223372036854775807n
        || sequence !== undefined && BigInt(event.sequence) !== sequence + 1n) return null;
      notificationInstant(event.createdAt);
      sequence = BigInt(event.sequence); const id = event.eventId.toLowerCase(); local.add(id); eventIds.push(id);
    }
    return { cursor: page.cursor, resetRequired: false, lastSequence: sequence, eventIds };
  } catch { return null; }
}
