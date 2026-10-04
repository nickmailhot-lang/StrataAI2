import { apiFetch } from '../../api/apiFetch';

export type ActivityAction = 'board_read' | 'card_read' | 'board_disclosure' | 'card_disclosure'
  | 'comment_disclosure' | 'comment_read' | 'comment_create' | 'comment_edit' | 'comment_delete'
  | 'mention_read' | 'mention_selection' | 'card_group_confirmation' | 'board_group_confirmation'
  | 'search_disclosure' | 'search_read' | 'notification_disclosure' | 'notification_read' | 'notification_mark_read';
type Kind = 'open' | 'use' | 'retry' | 'exception' | 'conflict' | 'reconnect' | 'success' | 'failure';
type Observation = { action: ActivityAction; kind: Kind; count: number; durationMs?: number };
const actions = new Set<string>(['board_read', 'card_read', 'board_disclosure', 'card_disclosure',
  'comment_disclosure', 'comment_read', 'comment_create', 'comment_edit', 'comment_delete',
  'mention_read', 'mention_selection', 'card_group_confirmation', 'board_group_confirmation', 'search_disclosure', 'search_read',
  'notification_disclosure', 'notification_read', 'notification_mark_read']);
const kinds = new Set<string>(['open', 'use', 'retry', 'exception', 'conflict', 'reconnect', 'success', 'failure']);
let enabled = import.meta.env.PROD;
let queue: Observation[] = []; let timer: ReturnType<typeof setTimeout> | undefined;
let pending: AbortController | undefined; let generation = 0;

// Also used by isolated tests. No identity/scope/key/content is retained here.
export function configureActivityTelemetry(value: boolean) {
  enabled = value; generation++; queue = []; clearTimeout(timer); timer = undefined; pending?.abort(); pending = undefined;
}
function schedule() {
  if (enabled && queue.length && !timer && !pending) timer = setTimeout(() => { timer = undefined; void flushActivityTelemetry(); }, 5000);
}
export function activityEvent(action: ActivityAction, kind: Kind) {
  if (!enabled || !actions.has(action) || !kinds.has(kind) || kind === 'success' || kind === 'failure') return;
  enqueue({ action, kind, count: 1 });
}
export function activityResult(action: ActivityAction, success: boolean, started: number) {
  if (!enabled || !actions.has(action) || !Number.isFinite(started)) return;
  const durationMs = performance.now() - started;
  if (!Number.isFinite(durationMs) || durationMs < 0 || durationMs > 60000) return;
  enqueue({ action, kind: success ? 'success' : 'failure', count: 1, durationMs });
}
function enqueue(observation: Observation) {
  if (observation.durationMs === undefined) {
    const existing = queue.find(entry => entry.action === observation.action && entry.kind === observation.kind && entry.durationMs === undefined && entry.count < 100);
    if (existing) { existing.count++; schedule(); return; }
  }
  if (queue.length === 120) queue.shift();
  queue.push(observation); schedule();
}
export async function flushActivityTelemetry() {
  if (!enabled || pending || !queue.length) return;
  clearTimeout(timer); timer = undefined;
  const batch = queue.splice(0, 20); const ticket = generation;
  const controller = new AbortController(); pending = controller;
  const timeout = setTimeout(() => controller.abort(), 3000);
  try {
    await apiFetch('/me/activity-client-events', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ events: batch }), signal: controller.signal });
  } catch { /* Best effort. Never retry telemetry or alter an authoritative interaction. */ }
  finally { clearTimeout(timeout); if (ticket === generation) { pending = undefined; schedule(); } }
}
