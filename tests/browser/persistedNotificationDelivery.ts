import { execFileSync } from 'node:child_process';
import type { APIRequestContext } from '@playwright/test';
import { expect } from './releaseTest';

export type PrivateNotificationEnvelope = {
  eventId: string; eventType: string; actorId: string; recipientId: string;
  organizationId: string; boardId: string; entityType: string; entityId: string;
  version: number; sequence: string; createdAt: string; metadata: Record<string, unknown>;
};

function utc(value: string) {
  expect(value).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/);
  return value.replace(/\+00:00$/, 'Z').replace(/(\.\d*?)0+Z$/, '$1Z').replace(/\.Z$/, 'Z');
}

export function retainPrivateNotification(event: PrivateNotificationEnvelope, organizationId: string, recipientId: string) {
  expect(Object.keys(event).sort()).toEqual(['eventId', 'eventType', 'actorId', 'recipientId', 'organizationId',
    'boardId', 'entityType', 'entityId', 'version', 'sequence', 'createdAt', 'metadata'].sort());
  expect(event).toMatchObject({ organizationId, recipientId, entityType: 'Notification', metadata: {} });
  expect(['NOTIFICATION_CREATED', 'NOTIFICATION_READ']).toContain(event.eventType);
  expect(event.version).toBe(event.eventType === 'NOTIFICATION_CREATED' ? 1 : 2);
  expect(event.sequence).toMatch(/^[1-9][0-9]*$/); utc(event.createdAt);
  return event;
}

// Read-only independent persistence oracle. Never fabricate a notification,
// clock or journal event to establish a producer-to-native-client contract.
export async function expectPersistedNotificationDelivery(request: APIRequestContext, organizationId: string,
  recipientId: string, streams: PrivateNotificationEnvelope[][], excludedStreamIds: string[] = []) {
  expect(process.env.CI).toBe('true');
  for (const id of [organizationId, recipientId]) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  const query = (sql: string) => execFileSync('docker', ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres',
    'sh', '-c', 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'],
  { input: sql, encoding: 'utf8', stdio: 'pipe' }).trim();
  const scope = `tenant_id='${organizationId}' AND recipient_id='${recipientId}'`;
  const stored = JSON.parse(query(`SELECT jsonb_agg(to_jsonb(n) ORDER BY created_at DESC,id DESC) FROM card_assignment_notifications n WHERE ${scope};`));
  const inbox = await request.get(`/organizations/${organizationId}/notifications`); expect(inbox.status()).toBe(200);
  const rows = (await inbox.json()).items; expect(rows).toHaveLength(stored.length);
  for (const row of rows) {
    const persisted = stored.find((item: { id: string }) => item.id === row.id);
    expect(persisted).toMatchObject({ tenant_id: organizationId, recipient_id: recipientId, actor_id: row.actorId,
      board_id: row.boardId, card_id: row.entityId, notification_type: row.type });
    expect(row.entityType).toBe('Card');
    expect(row.entityLink).toBe(`/app/${organizationId}/boards/${row.boardId}/cards/${row.entityId}`);
    expect(utc(row.createdAt)).toBe(utc(persisted.created_at));
    expect(utc(row.updatedAt)).toBe(utc(persisted.read_at ?? persisted.created_at));
    if (persisted.read_at === null) expect(row.readAt).toBeNull();
    else expect(utc(row.readAt)).toBe(utc(persisted.read_at));
  }
  const sourceCount = query(`SELECT count(*) FROM card_assignment_notifications n JOIN work_events e
    ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE n.${scope.replace(' AND recipient_id', ' AND n.recipient_id')}
    AND e.actor_id=n.actor_id AND e.board_id=n.board_id AND e.event_type=n.source_event_type AND e.created_at=n.created_at
    AND (e.entity_type='Card' AND e.entity_id=n.card_id AND e.entity_version=n.card_version
      OR e.entity_type='Reminder' AND n.notification_type='REMINDER_FIRED' AND EXISTS
        (SELECT FROM card_reminders r WHERE r.tenant_id=n.tenant_id AND r.id=e.entity_id AND r.card_id=n.card_id AND r.version=e.entity_version));`);
  expect(sourceCount).toBe(String(stored.length));
  const persistedJournal = JSON.parse(query(`SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM notification_events e WHERE ${scope};`));
  const sync = await request.get(`/organizations/${organizationId}/notifications/sync?after=0`); expect(sync.status()).toBe(200);
  const journal = (await sync.json()).events as PrivateNotificationEnvelope[];
  expect(journal).toHaveLength(stored.length + stored.filter((row: { read_at: string | null }) => row.read_at !== null).length);
  expect(persistedJournal).toHaveLength(journal.length);
  for (const [index, event] of journal.entries()) {
    retainPrivateNotification(event, organizationId, recipientId);
    const persisted = persistedJournal[index]; const notification = stored.find((item: { id: string }) => item.id === event.entityId);
    expect(event).toEqual({ eventId: persisted.event_id, eventType: persisted.event_type, actorId: persisted.actor_id,
      recipientId: persisted.recipient_id, organizationId: persisted.tenant_id, boardId: persisted.board_id,
      entityType: 'Notification', entityId: persisted.notification_id, version: persisted.version,
      sequence: String(persisted.sequence), createdAt: event.createdAt, metadata: persisted.metadata });
    expect(event.sequence).toBe(String(index + 1));
    const isRead = event.eventType === 'NOTIFICATION_READ';
    expect(event.actorId).toBe(isRead ? recipientId : notification.actor_id); expect(event.boardId).toBe(notification.board_id);
    expect(utc(event.createdAt)).toBe(utc(persisted.created_at));
    expect(utc(event.createdAt)).toBe(utc(isRead ? notification.read_at : notification.created_at));
  }
  const expected = journal.filter(event => !excludedStreamIds.includes(event.entityId));
  const canonical = (event: PrivateNotificationEnvelope) => ({ ...event, createdAt: utc(event.createdAt) });
  for (const stream of streams) expect(stream.map(canonical)).toEqual(expected.map(canonical));
}
