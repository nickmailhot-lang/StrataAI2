type Subject = { id: string; version: number };
export type IdentitySync<T> = { profile: T; cursor: number; latestSequence: number; hasMore: boolean; eventIds: string[] };

export function validateIdentitySync<T extends Subject>(value: unknown, after: number | undefined,
  isProfile: (value: unknown) => value is T, seen: ReadonlySet<string>): IdentitySync<T> | undefined {
  if (!value || typeof value !== 'object') return;
  const data = value as Record<string, unknown>;
  if (!isProfile(data.profile) || !Number.isSafeInteger(data.cursor) || !Number.isSafeInteger(data.latestSequence)
    || typeof data.hasMore !== 'boolean' || !Array.isArray(data.events) || data.events.length > 100) return;
  const profile = data.profile;
  const cursor = data.cursor as number;
  const latest = data.latestSequence as number;
  if (cursor < 0 || latest < cursor || data.hasMore !== (cursor < latest)) return;
  if (after === undefined) {
    if (data.events.length || cursor !== latest) return;
    return { profile, cursor, latestSequence: latest, hasMore: false, eventIds: [] };
  }
  if (cursor < after || cursor - after !== data.events.length || (data.hasMore && data.events.length !== 100)) return;
  const ids = new Set<string>();
  for (let index = 0; index < data.events.length; index++) {
    const item = data.events[index] as Record<string, unknown> | null;
    if (!item || typeof item !== 'object' || typeof item.eventId !== 'string'
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(item.eventId)
      || ids.has(item.eventId) || seen.has(item.eventId) || item.sequence !== after + index + 1
      || !['USER_REGISTERED', 'USER_PROFILE_UPDATED', 'USER_DEACTIVATED', 'SESSION_REVOKED', 'EMAIL_VERIFIED'].includes(item.eventType as string)
      || item.actorId !== profile.id || item.entityId !== profile.id || item.entityType !== 'User'
      || item.organizationId !== null || item.boardId !== null
      || !Number.isSafeInteger(item.version) || (item.version as number) < 1 || (item.version as number) > profile.version
      || !item.metadata || typeof item.metadata !== 'object' || Array.isArray(item.metadata) || Object.keys(item.metadata).length
      || typeof item.createdAt !== 'string' || !Number.isFinite(Date.parse(item.createdAt))) return;
    ids.add(item.eventId);
  }
  return { profile, cursor, latestSequence: latest, hasMore: data.hasMore, eventIds: [...ids] };
}
