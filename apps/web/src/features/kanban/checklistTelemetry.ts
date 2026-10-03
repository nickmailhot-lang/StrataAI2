import { apiFetch } from '../../api/apiFetch';

export type ChecklistAction = 'read' | 'item_read' | 'create' | 'rename' | 'delete' | 'position' | 'item_create' | 'item_update' | 'item_delete' | 'item_position' | 'disclosure' | 'item_disclosure' | 'realtime';
type Kind = 'open' | 'use' | 'retry' | 'exception' | 'conflict' | 'reconnect' | 'success' | 'failure';
type Observation = { action: ChecklistAction; kind: Kind; count: number; durationMs?: number };
const actions = new Set<string>(['read', 'item_read', 'create', 'rename', 'delete', 'position', 'item_create', 'item_update', 'item_delete', 'item_position', 'disclosure', 'item_disclosure', 'realtime']);
const kinds = new Set<string>(['open', 'use', 'retry', 'exception', 'conflict', 'reconnect', 'success', 'failure']);
let enabled = import.meta.env.PROD;
let queue: Observation[] = []; let timer: ReturnType<typeof setTimeout> | undefined;
let pending: AbortController | undefined; let generation = 0;

// Also used by isolated tests. No identity/scope/key/content is retained here.
export function configureChecklistTelemetry(value: boolean) {
  enabled = value; generation++; queue = []; clearTimeout(timer); timer = undefined; pending?.abort(); pending = undefined;
}
function schedule() {
  if (enabled && queue.length && !timer && !pending) timer = setTimeout(() => { timer = undefined; void flushChecklistTelemetry(); }, 5000);
}
export function checklistEvent(action: ChecklistAction, kind: Kind) {
  if (!enabled || !actions.has(action) || !kinds.has(kind) || kind === 'success' || kind === 'failure') return;
  enqueue({ action, kind, count: 1 });
}
export function checklistResult(action: ChecklistAction, success: boolean, started: number) {
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
export async function flushChecklistTelemetry() {
  if (!enabled || pending || !queue.length) return;
  clearTimeout(timer); timer = undefined;
  const batch = queue.splice(0, 20); const ticket = generation;
  const controller = new AbortController(); pending = controller;
  const timeout = setTimeout(() => controller.abort(), 3000);
  try {
    await apiFetch('/me/checklist-client-events', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ events: batch }), signal: controller.signal });
  } catch { /* Best effort. Never retry telemetry or alter an authoritative interaction. */ }
  finally { clearTimeout(timeout); if (ticket === generation) { pending = undefined; schedule(); } }
}
