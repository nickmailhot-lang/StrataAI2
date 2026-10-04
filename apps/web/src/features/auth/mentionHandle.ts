export type MentionHandleSetting = { userId: string; handle: string; userVersion: number; handleVersion: number;
  createdAt: string; updatedAt: string };
export type MentionHandleAcknowledgment = { userId: string; handle: string; userVersion: number; handleVersion: number; changed: boolean };
export type MentionHandleIntent = Readonly<{ original: Readonly<MentionHandleSetting>; handle: string; key: string; body: string }>;
const invalid = () => new Error('Invalid account handle response');
const uuid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';
const revision = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) > 0;
function record(value: unknown, fields: string[]) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw invalid();
  const row = value as Record<string, unknown>;
  if (Object.keys(row).length !== fields.length || fields.some(field => !Object.hasOwn(row, field))) throw invalid();
  return row;
}
function instant(value: unknown) {
  if (typeof value !== 'string') throw invalid();
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,6}))?(?:Z|\+00:00)$/.exec(value);
  if (!match || match[1].startsWith('0000')) throw invalid();
  const milliseconds = Date.parse(match[1] + 'Z');
  if (!Number.isFinite(milliseconds) || new Date(milliseconds).toISOString().slice(0, 19) !== match[1]) throw invalid();
  return BigInt(milliseconds) * 1000n + BigInt((match[2] ?? '').padEnd(6, '0'));
}
export function normalizeMentionHandle(value: string, subject: string) {
  if (!uuid(subject) || typeof value !== 'string' || value.length > 40) throw invalid();
  // .NET whitespace differs from JavaScript trim (notably FEFF).
  const whitespace = '[\\u0009-\\u000d\\u0020\\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000]';
  const handle = value.replace(new RegExp(`^${whitespace}+|${whitespace}+$`, 'g'), '').toLowerCase();
  if (!/^[a-z][a-z0-9_]{2,39}$/.test(handle) || ['card', 'board'].includes(handle)
    || handle.startsWith('u_') && handle !== `u_${subject.replaceAll('-', '')}`) throw invalid();
  return handle;
}
export function parseMentionHandleSetting(value: unknown, subject: string, userVersion: number): MentionHandleSetting {
  const row = record(value, ['userId', 'handle', 'userVersion', 'handleVersion', 'createdAt', 'updatedAt']);
  if (!uuid(subject) || row.userId !== subject || !revision(userVersion) || row.userVersion !== userVersion
    || !revision(row.handleVersion) || row.handleVersion > userVersion || typeof row.handle !== 'string'
    || normalizeMentionHandle(row.handle, subject) !== row.handle) throw invalid();
  const created = instant(row.createdAt); const updated = instant(row.updatedAt);
  if (updated < created || row.handleVersion === 1 && updated !== created) throw invalid();
  return row as MentionHandleSetting;
}
export function createMentionHandleIntent(setting: MentionHandleSetting, proposed: string, key: string = crypto.randomUUID()): MentionHandleIntent {
  if (!uuid(key)) throw invalid();
  const original = Object.freeze({ ...parseMentionHandleSetting(setting, setting.userId, setting.userVersion) });
  const handle = normalizeMentionHandle(proposed, original.userId);
  if (handle !== original.handle && (!Number.isSafeInteger(original.userVersion + 1) || !Number.isSafeInteger(original.handleVersion + 1))) throw invalid();
  const body = JSON.stringify({ handle, userVersion: original.userVersion, handleVersion: original.handleVersion });
  return Object.freeze({ original, handle, key, body });
}
export function parseMentionHandleAcknowledgment(value: unknown, intent: MentionHandleIntent): MentionHandleAcknowledgment {
  const row = record(value, ['userId', 'handle', 'userVersion', 'handleVersion', 'changed']);
  const original = parseMentionHandleSetting(intent.original, intent.original.userId, intent.original.userVersion);
  const handle = normalizeMentionHandle(intent.handle, original.userId); const changed = handle !== original.handle;
  const delta = changed ? 1 : 0;
  if (!uuid(intent.key) || handle !== intent.handle || intent.body !== JSON.stringify({ handle, userVersion: original.userVersion, handleVersion: original.handleVersion })
    || row.userId !== original.userId || row.handle !== handle || row.changed !== changed
    || !revision(row.userVersion) || row.userVersion !== original.userVersion + delta
    || !revision(row.handleVersion) || row.handleVersion !== original.handleVersion + delta) throw invalid();
  return row as MentionHandleAcknowledgment;
}
