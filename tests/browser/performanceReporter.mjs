import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const statuses = new Set(['passed', 'failed', 'timedOut', 'skipped', 'interrupted']);
const duration = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;

// Never serialize test titles, errors, request data or arbitrary fields.
export function performanceEntry(name, value, status) {
  if (!statuses.has(status) || !value || typeof value !== 'object') return undefined;
  const feedback = value.feedbackObserved === true && duration(value.feedbackMs) ? value.feedbackMs
    : value.feedbackObserved === false && value.feedbackMs === null ? null : undefined;
  if (feedback === undefined || value.fixture?.viewport !== '1280x844'
    || value.fixture?.topology !== 'exact release images through Nginx') return undefined;
  if (name === 'list-feedback-performance.json' && value.fixture.lists === 2 && value.fixture.cards === 0) {
    return { metric: 'desktop-list-feedback', status, fixture: { lists: 2, cards: 0, viewport: '1280x844' },
      feedbackObserved: feedback !== null, feedbackMs: feedback, budgetsMs: { feedback: 100 } };
  }
  if (name !== 'kanban-performance.json' || value.fixture.lists !== 3 || value.fixture.cards !== 50
    || value.fixture.samples !== 20 || value.fixture.assets !== 'warm'
    || ![value.usableMs, value.detailMs, value.mutationP95Ms].every(duration)
    || !Array.isArray(value.mutationSamplesMs) || value.mutationSamplesMs.length !== 20
    || !value.mutationSamplesMs.every(duration)) return undefined;
  const p95 = [...value.mutationSamplesMs].sort((a, b) => a - b)[18];
  if (p95 !== value.mutationP95Ms) return undefined;
  return { metric: 'normal-desktop-kanban', status,
    fixture: { lists: 3, cards: 50, samples: 20, viewport: '1280x844', assets: 'warm' },
    usableMs: value.usableMs, detailMs: value.detailMs, feedbackObserved: feedback !== null, feedbackMs: feedback,
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
      if (!['kanban-performance.json', 'list-feedback-performance.json'].includes(attachment.name)
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
    const revision = /^[0-9a-f]{40,64}$/.test(process.env.GITHUB_SHA ?? '') ? process.env.GITHUB_SHA : null;
    await writeFile(resolve(directory, 'kanban.json'), JSON.stringify({ schemaVersion: 1, revision,
      topology: 'exact release images through Nginx', measurements: this.entries }, null, 2));
  }
}
