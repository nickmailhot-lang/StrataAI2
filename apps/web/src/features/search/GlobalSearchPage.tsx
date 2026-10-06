import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseSearchPage, type SearchPage } from './globalSearch';
import { formatUserDateTime } from '../auth/userDateTime';
import { formatCardDate } from '../kanban/cardDates';
import { activityEvent, activityResult } from '../kanban/activityTelemetry';
import { ChangedSearchInteractionActor, SearchInteractionAcknowledgments } from './searchInteraction';

type Criteria = { q: string; label: string; member: string; match: 'all' | 'any'; scope: 'active' | 'archived' };
const empty = (): Criteria => ({ q: '', label: '', member: '', match: 'all', scope: 'active' });
class ChangedSearchAccount extends Error {}
export function GlobalSearchPage() {
  const [draft, setDraft] = useState<Criteria>(empty); const [page, setPage] = useState<SearchPage & { locale: string; timezone: string }>();
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const [acknowledged, setAcknowledged] = useState(false);
  const acknowledgments = useRef(new SearchInteractionAcknowledgments());
  const applied = useRef<Criteria>(empty()); const cursor = useRef<string | undefined>(undefined);
  const actor = useRef<string | undefined>(undefined); const pending = useRef<AbortController | undefined>(undefined);
  const epoch = useRef(0); const alive = useRef(false);
  const load = useCallback(async (criteria: Criteria, after?: string, kind: 'use' | 'retry' | 'reconnect' = 'use') => {
    pending.current?.abort(); const controller = new AbortController(); pending.current = controller;
    const ticket = ++epoch.current; setBusy(true); setPage(undefined); setNotice(undefined); setAcknowledged(false);
    const started = performance.now(); activityEvent('search_read', kind);
    try {
      const result = await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(before)) throw new Error('Current account unavailable');
        if (actor.current && actor.current !== before.id.toLowerCase()) throw new ChangedSearchAccount();
        const query = new URLSearchParams(criteria); if (after) query.set('after', after);
        const response = await workRequest<unknown>(`/search?${query.toString()}`, { signal });
        const afterProfile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(afterProfile) || before.id.toLowerCase() !== afterProfile.id.toLowerCase())
          throw new ChangedSearchAccount();
        return { actor: afterProfile.id.toLowerCase(), page: { ...parseSearchPage(response, afterProfile.id, after), locale: afterProfile.locale, timezone: afterProfile.timezone } };
      }, controller.signal);
      if (!alive.current || ticket !== epoch.current || controller.signal.aborted) return;
      const freshAcknowledgment = acknowledgments.current.consume(result.page.interaction);
      activityResult('search_read', true, started);
      actor.current = result.actor; applied.current = criteria; cursor.current = after; setPage(result.page);
      setAcknowledged(freshAcknowledgment);
    } catch (reason) {
      if (!alive.current || ticket !== epoch.current) return;
      activityResult('search_read', false, started);
      if (!(reason instanceof WorkRequestError) && !(reason instanceof ChangedSearchAccount) && !(reason instanceof ChangedSearchInteractionActor)) activityEvent('search_read', 'exception');
      setPage(undefined);
      if (reason instanceof ChangedSearchAccount || reason instanceof ChangedSearchInteractionActor || reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) {
        actor.current = undefined; cursor.current = undefined; applied.current = empty(); setDraft(empty());
        acknowledgments.current.clear();
      }
      setNotice('Search is unavailable. Check your account and access, then search again.');
    } finally {
      if (alive.current && ticket === epoch.current) { pending.current = undefined; setBusy(false); }
    }
  }, []);
  useEffect(() => {
    alive.current = true;
    activityEvent('search_disclosure', 'open');
    const refresh = () => { if (!pending.current && document.visibilityState !== 'hidden' && actor.current) void load(applied.current, cursor.current); };
    const online = () => { if (!pending.current && document.visibilityState !== 'hidden' && actor.current) void load(applied.current, cursor.current, 'reconnect'); };
    window.addEventListener('focus', refresh); window.addEventListener('online', online);
    document.addEventListener('visibilitychange', refresh); const interval = setInterval(refresh, 10_000);
    return () => { alive.current = false; epoch.current++; pending.current?.abort(); clearInterval(interval);
      window.removeEventListener('focus', refresh); window.removeEventListener('online', online); document.removeEventListener('visibilitychange', refresh); };
  }, [load]);
  return <Stack spacing={2} component="section" aria-label="Global Card search">
    <Typography variant="h5" component="h2">Search Cards</Typography>
    <Typography>Search across your accessible Organizations and Boards.</Typography>
    <Stack component="form" spacing={2} onSubmit={event => { event.preventDefault(); actor.current = undefined;
      void load({ ...draft, q: draft.q.trim(), label: draft.label.trim(), member: draft.member.trim() }); }}>
      <TextField label="Card text" value={draft.q} onChange={e => setDraft({ ...draft, q: e.target.value })} slotProps={{ htmlInput: { maxLength: 160 } }} />
      <TextField label="Label name" value={draft.label} onChange={e => setDraft({ ...draft, label: e.target.value })} slotProps={{ htmlInput: { maxLength: 160 } }} />
      <TextField label="Member name" value={draft.member} onChange={e => setDraft({ ...draft, member: e.target.value })} slotProps={{ htmlInput: { maxLength: 160 } }} />
      <TextField select label="Match criteria" value={draft.match} onChange={e => setDraft({ ...draft, match: e.target.value as Criteria['match'] })}>
        <MenuItem value="all">Match all</MenuItem><MenuItem value="any">Match any</MenuItem>
      </TextField>
      <TextField select label="Search scope" value={draft.scope} onChange={e => setDraft({ ...draft, scope: e.target.value as Criteria['scope'] })}>
        <MenuItem value="active">Active work</MenuItem><MenuItem value="archived">Archived work</MenuItem>
      </TextField>
      <Button type="submit" variant="contained" disabled={busy}>Search</Button>
    </Stack>
    <Stack role="status" aria-live="polite">{busy && <Typography>Searching…</Typography>}
      {notice && <Alert severity="warning">{notice}</Alert>}
      {page && <Typography>{page.items.length} results on this page.{page.nextCursor ? ' More work can be searched.' : ' Search complete.'}</Typography>}
      {acknowledged && <Typography>Search acknowledged.</Typography>}
    </Stack>
    {page?.items.map(item => <Paper key={item.id} variant="outlined" sx={{ p: 2 }}>
      <Typography component={Link} to={`/app/${item.organizationId}/boards/${item.boardId}/cards/${item.id}`}>{item.title}</Typography>
      <Typography>{item.boardName} / {item.listName}</Typography>
      <Typography>Labels: {item.labels.join(', ') || 'None'}{item.moreLabels ? ' (more on Card)' : ''}</Typography>
      <Typography>Members: {item.members.join(', ') || 'None'}{item.moreMembers ? ' (more on Card)' : ''}</Typography>
      <Typography>{item.dueAt ? `Due ${item.dueHasTime
        ? formatUserDateTime(item.dueAt, { locale: page.locale, timezone: item.boardDateTimezone ?? page.timezone }) ?? 'Date unavailable'
        : formatCardDate(item.dueAt, false, page.locale, item.boardDateTimezone ?? page.timezone)}${item.dueComplete ? ' — completed' : ''}` : 'No deadline'}</Typography>
    </Paper>)}
    {page?.nextCursor && <Button disabled={busy} onClick={() => void load(applied.current, page.nextCursor!)}>Next search page</Button>}
    {page && <Button disabled={busy} onClick={() => void load(applied.current, cursor.current, 'retry')}>Refresh results</Button>}
  </Stack>;
}
