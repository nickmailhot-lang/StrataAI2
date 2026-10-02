import { readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

// Input is a complete GitHub issue snapshot, not a model-generated status list.
const [input, output] = process.argv.slice(2);
if (!input || !output) throw new Error('Usage: node scripts/analyze-ticket-dependencies.mjs issues.json output.md');
const issues = JSON.parse(readFileSync(input, 'utf8'));
if (!Array.isArray(issues)) throw new Error('Expected an issue array');
const duplicates = issues.filter(issue => issue.state === 'closed' && issue.state_reason === 'duplicate');
const tickets = issues.filter(issue => !duplicates.includes(issue)).map(issue => {
  const id = /^\[((?:PRD|ARCH)-\d{2})\]/.exec(issue.title)?.[1];
  if (!id || !Number.isInteger(issue.number) || typeof issue.body !== 'string') throw new Error(`Invalid ticket ${issue.number}`);
  if (!['open', 'closed'].includes(issue.state)) throw new Error(`Missing authoritative issue state: ${id}`);
  const section = /\n## (?:21\. )?Dependencies\s*\n([\s\S]*?)(?=\n## |$)/.exec(issue.body)?.[1];
  if (section === undefined) throw new Error(`Missing dependency section: ${id}`);
  const expanded = section.replace(/\b((?:ARCH|PRD)-)(\d{2})\s+through\s+((?:ARCH|PRD)-)(\d{2})\b/g, (_, first, start, last, end) => {
    if (first !== last || +start > +end) throw new Error(`Unsupported range in ${id}`);
    return Array.from({ length: +end - +start + 1 }, (__, index) => `${first}${String(+start + index).padStart(2, '0')}`).join(' ');
  });
  return { ...issue, id, dependencies: [...new Set(expanded.match(/\b(?:PRD|ARCH)-\d{2}\b/g) ?? [])].sort(),
    requirements: [...new Set(issue.body.match(/\b[A-Z][A-Z0-9_-]*-FR-\d{3}\b/g) ?? [])],
    acceptance: [...new Set(issue.body.match(/\bAC-[A-Z0-9-]+-\d{2}\b/g) ?? [])],
    digest: createHash('sha256').update(issue.body).digest('hex') };
}).sort((a, b) => a.id.localeCompare(b.id));
const byId = new Map(tickets.map(ticket => [ticket.id, ticket]));
if (byId.size !== tickets.length) throw new Error('Duplicate PRD/architecture identifiers');
for (const duplicate of duplicates) {
  const id = /^\[((?:PRD|ARCH)-\d{2})\]/.exec(duplicate.title)?.[1];
  if (!id || !byId.has(id)) throw new Error(`Closed duplicate lacks canonical definition: ${duplicate.number}`);
}
for (const ticket of tickets) for (const dependency of ticket.dependencies)
  if (!byId.has(dependency)) throw new Error(`Dependency ${dependency} of ${ticket.id} is absent; include its authoritative state before ordering`);

// Tarjan components collapse reciprocal dependencies before dependency-first DFS.
let index = 0;
const stack = [], active = new Set(), indices = new Map(), low = new Map(), groups = [];
function visit(id) {
  indices.set(id, index); low.set(id, index++); stack.push(id); active.add(id);
  for (const dependency of byId.get(id).dependencies) {
    if (!indices.has(dependency)) { visit(dependency); low.set(id, Math.min(low.get(id), low.get(dependency))); }
    else if (active.has(dependency)) low.set(id, Math.min(low.get(id), indices.get(dependency)));
  }
  if (low.get(id) === indices.get(id)) {
    const group = []; let current;
    do { current = stack.pop(); active.delete(current); group.push(current); } while (current !== id);
    groups.push(group.sort());
  }
}
for (const ticket of tickets) if (!indices.has(ticket.id)) visit(ticket.id);
const groupOf = new Map(groups.flatMap((group, n) => group.map(id => [id, n])));
const ordered = [], visited = new Set();
function order(n) {
  if (visited.has(n)) return;
  visited.add(n);
  for (const id of groups[n]) for (const dependency of byId.get(id).dependencies)
    if (groupOf.get(dependency) !== n) order(groupOf.get(dependency));
  ordered.push(n);
}
for (let n = 0; n < groups.length; n++) order(n);
const links = ids => ids.map(id => `[${id}](${byId.get(id).html_url})`).join(', ') || 'None';
const lines = ['# Ticket dependency inventory', '',
  `Snapshot: ${tickets.length} PRD/architecture tickets; ${tickets.filter(ticket => ticket.state === 'open').length} open and ${tickets.filter(ticket => ticket.state === 'closed').length} closed. Source: GitHub issue bodies and states; individual update timestamps and SHA-256 hashes below bind this inventory to the inspected definitions.`, '',
  'Generated with `node scripts/analyze-ticket-dependencies.mjs <complete-issues.json> docs/ticket-dependency-map.md`. Refresh the complete issue snapshot before relying on it. The generator rejects missing dependency definitions rather than treating them as completed.', '',
  'This inventories requirements and orders dependencies; it does not prove implementation or acceptance. Closed dependency definitions remain included so ordering preserves the complete scope. Reported GitHub state is not a substitute for closure evidence. Code, executed tests, release-image evidence, accessibility, telemetry and performance must still be assessed against the full linked ticket, including unnumbered rules and Definition of Done.', '',
  ...(duplicates.length ? [`GitHub reports these closed duplicates; their canonical definitions remain in the inventory: ${duplicates.map(issue => `[#${issue.number}](${issue.html_url})`).join(', ')}.`, ''] : []),
  '## Dependency groups', '',
  'Groups below are dependency-first. A group with multiple tickets contains cycles and must be developed as an integrated dependency set; there is no valid strict ticket-by-ticket topological order within it. This does not relax any requirement or authorize closing a dependent ticket prematurely.', ''];
ordered.forEach((n, position) => lines.push(`${position + 1}. ${links(groups[n])}`));
lines.push('', '## Ticket definitions', '', '| Ticket | GitHub state | Direct dependencies | Distinct FR IDs | Distinct AC IDs | Issue updated | Body SHA-256 |', '| --- | --- | --- | ---: | ---: | --- | --- |');
for (const ticket of tickets) lines.push(`| [${ticket.id}: ${ticket.title.replace(/^\[[^\]]+\]\s*/, '').replaceAll('|', '\\|')}](${ticket.html_url}) | ${ticket.state} | ${ticket.dependencies.join(', ') || 'None'} | ${ticket.requirements.length} | ${ticket.acceptance.length} | ${ticket.updated_at} | ${ticket.digest} |`);
lines.push('', '## Working order inside the foundational dependency group', '',
  'Build and verify the shared runtime, tenant/session/authorization/audit boundaries and immutable release pipeline first. Complete identity, Organization, Board sharing and canonical work behavior against those boundaries, then Kanban/list/card interaction and lifecycle. Extend security and tests alongside each domain. Proceed to metadata, assignments, dates, checklists, attachments, activity, search and notifications before the dependent strata, records, governance, portal, reporting and AI domains. Revisit the exact linked dependencies for each change.', '',
  'A passing foundational increment permits further implementation, not issue closure. Full ticket acceptance may require later members of the same dependency group. Keep missing FR/AC/test/DoD evidence explicit and preserve the MUI, ASP.NET modular monolith, separate Worker, PostgreSQL/RLS/pgvector, object storage and build-once CI architecture.', '');
writeFileSync(output, lines.join('\n'), 'utf8');
process.stdout.write(`${tickets.length} tickets, ${groups.length} dependency groups, ${groups.filter(group => group.length > 1).length} cyclic groups\n`);
