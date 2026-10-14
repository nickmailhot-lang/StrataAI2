import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const statuses = new Set(['passed', 'failed', 'timedOut', 'skipped', 'interrupted']);
const duration = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;

function detailEvidence(value, status) {
  const mode = value.fixture?.detailTiming;
  if (mode === undefined) return duration(value.detailMs)
    ? { fixture: { detailTiming: null }, fields: { detailMs: value.detailMs } } : undefined;
  if (mode !== 'browser-painted-editable' || !duration(value.detailAutomationMs)
    || typeof value.detailObserved !== 'boolean'
    || (value.detailObserved ? !duration(value.detailMs) : value.detailMs !== null || status === 'passed')) return undefined;
  return { fixture: { detailTiming: mode }, fields: {
    detailMs: value.detailMs, detailObserved: value.detailObserved, detailAutomationMs: value.detailAutomationMs } };
}

// Local diagnostic runs use the same scenarios, but cannot certify release images.
export function performanceProvenance(environment = {}) {
  const revision = /^[0-9a-f]{40,64}$/.test(environment.GITHUB_SHA ?? '') ? environment.GITHUB_SHA : null;
  return { revision, topology: environment.CI === 'true' && revision
    ? 'exact release images through Nginx' : 'unverified runtime' };
}

// Never serialize test titles, errors, request data or arbitrary fields.
export function performanceEntry(name, value, status) {
  if (!statuses.has(status) || !value || typeof value !== 'object') return undefined;
  const tracing = value.fixture?.tracing ?? null;
  if (tracing !== null && !['commands-and-network', 'dom-snapshots'].includes(tracing)) return undefined;
  if (name === 'card-dates-performance.json') {
    const detail = detailEvidence(value, status);
    if (!detail) return undefined;
    if (value.fixture?.lists !== 3 || value.fixture.cards !== 50 || value.fixture.datedCards !== 50
      || value.fixture.samples !== 20 || value.fixture.viewport !== '1280x844' || value.fixture.assets !== 'warm'
      || value.fixture.topology !== 'exact release images through Nginx'
      || ![value.usableMs, value.mutationP95Ms].every(duration)
      || !Array.isArray(value.mutationSamplesMs) || value.mutationSamplesMs.length !== 20
      || !value.mutationSamplesMs.every(duration)) return undefined;
    const p95 = [...value.mutationSamplesMs].sort((a, b) => a - b)[18];
    if (p95 !== value.mutationP95Ms) return undefined;
    return { metric: 'normal-desktop-card-dates', status,
      fixture: { lists: 3, cards: 50, datedCards: 50, samples: 20, viewport: '1280x844', assets: 'warm', tracing, ...detail.fixture },
      usableMs: value.usableMs, ...detail.fields, mutationP95Ms: p95, mutationSamplesMs: [...value.mutationSamplesMs],
      budgetsMs: { usable: 1500, detail: 200, mutationP95: 500 } };
  }
  const feedback = value.feedbackObserved === true && duration(value.feedbackMs) ? value.feedbackMs
    : value.feedbackObserved === false && value.feedbackMs === null ? null : undefined;
  const viewport = ['kanban-performance.json', 'list-feedback-performance.json'].includes(name) && value.fixture?.viewport === '390x844' ? '390x844' : '1280x844';
  if (feedback === undefined || value.fixture?.viewport !== viewport
    || viewport === '390x844' && value.fixture.input !== 'chromium-touch'
    || value.fixture?.topology !== 'exact release images through Nginx') return undefined;
  if (name === 'list-feedback-performance.json' && value.fixture.lists === 2 && value.fixture.cards === 0) {
    return { metric: viewport === '390x844' ? 'phone-list-feedback' : 'desktop-list-feedback', status, fixture: { lists: 2, cards: 0, viewport, tracing, ...(viewport === '390x844' ? { input: 'chromium-touch' } : {}) },
      feedbackObserved: feedback !== null, feedbackMs: feedback, budgetsMs: { feedback: 100 } };
  }
  const checklist = name === 'checklist-performance.json';
  const detail = detailEvidence(value, status);
  if (!detail) return undefined;
  if ((!checklist && name !== 'kanban-performance.json') || value.fixture.lists !== 3 || value.fixture.cards !== 50
    || value.fixture.samples !== 20 || value.fixture.assets !== 'warm'
    || ![value.usableMs, value.mutationP95Ms].every(duration)
    || !Array.isArray(value.mutationSamplesMs) || value.mutationSamplesMs.length !== 20
    || !value.mutationSamplesMs.every(duration)) return undefined;
  const p95 = [...value.mutationSamplesMs].sort((a, b) => a - b)[18];
  if (p95 !== value.mutationP95Ms) return undefined;
  if (checklist && (value.fixture.checklists !== 2 || value.fixture.items !== 63 || value.fixture.pageSize !== 50
    || ![value.itemPageMs, value.nextItemPageMs].every(duration))) return undefined;
  return { metric: checklist ? 'normal-desktop-checklist' : viewport === '390x844' ? 'normal-phone-kanban' : 'normal-desktop-kanban', status,
    fixture: { lists: 3, cards: 50, samples: 20, viewport, assets: 'warm', tracing,
      ...detail.fixture,
      ...(viewport === '390x844' ? { input: 'chromium-touch' } : {}),
      ...(checklist ? { checklists: 2, items: 63, pageSize: 50 } : {}) },
    ...(checklist ? { itemPageMs: value.itemPageMs, nextItemPageMs: value.nextItemPageMs } : {}),
    usableMs: value.usableMs, ...detail.fields, feedbackObserved: feedback !== null, feedbackMs: feedback,
    mutationP95Ms: value.mutationP95Ms, mutationSamplesMs: [...value.mutationSamplesMs],
    budgetsMs: { usable: 1500, feedback: 100, detail: 200, mutationP95: 500 } };
}

export default class PerformanceReporter {
  entries = [];
  pending = [];
  onTestEnd(_test, result) {
    this.pending.push(this.collect(result));
  }
  async collect(result) {
    for (const attachment of result.attachments) {
      if (!['kanban-performance.json', 'list-feedback-performance.json', 'card-dates-performance.json', 'checklist-performance.json'].includes(attachment.name)
        || attachment.contentType !== 'application/json') continue;
      try {
        const body = attachment.body ?? await readFile(attachment.path);
        const entry = performanceEntry(attachment.name, JSON.parse(body.toString('utf8')), result.status);
        if (entry) this.entries.push(entry);
      } catch { /* Missing or invalid measurement remains absent, never fabricated. */ }
    }
  }
  async onEnd() {
    await Promise.all(this.pending);
    const directory = resolve('artifacts/browser-performance');
    await mkdir(directory, { recursive: true });
    await writeFile(resolve(directory, 'kanban.json'), JSON.stringify({ schemaVersion: 1,
      ...performanceProvenance(process.env), measurements: this.entries }, null, 2));
  }
}
