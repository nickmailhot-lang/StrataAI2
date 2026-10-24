import { PageHeader } from '../../components/PageHeader';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useLocation, useParams } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseSearchPage, type SearchPage } from './globalSearch';
import { formatUserDateTime } from '../auth/userDateTime';
import { formatCardDate } from '../kanban/cardDates';
import { activityEvent, activityResult } from '../kanban/activityTelemetry';
import { ChangedSearchInteractionActor, SearchInteractionAcknowledgments } from './searchInteraction';
import { workExperienceEnabled } from '../../app/workExperience';
import { readSearchReturn, forgetSearchReturn, retainSearchReturn, type SearchCriteria as Criteria, type SearchReturn } from './searchReturn';
import { focusBoardReturn } from '../kanban/focusBoardReturn';

const empty = (): Criteria => ({ q: '', label: '', member: '', match: 'all', scope: 'active' });
class ChangedSearchAccount extends Error {}
export function GlobalSearchPage() {
  const { organizationId } = useParams(); const location = useLocation();
  return <SearchContent key={`${organizationId}/${location.key}`} />;
}
function SearchContent() {
  const { organizationId } = useParams(); const location = useLocation();
  const [restore, setRestore] = useState(() => readSearchReturn(organizationId, location.key));
  const [draft, setDraft] = useState<Criteria>(empty); const [page, setPage] = useState<SearchPage & { locale: string; timezone: string }>();
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const [returning, setReturning] = useState(Boolean(restore)); const [returnNotice, setReturnNotice] = useState<string>();
  const initialReturn = useRef(restore); const returnFocus = useRef<string | undefined>(undefined);
  const returnFocusAllowed = useRef(false); const links = useRef(new Map<string, HTMLAnchorElement>());
  const [acknowledged, setAcknowledged] = useState(false);
  const acknowledgments = useRef(new SearchInteractionAcknowledgments());
  const applied = useRef<Criteria>(empty()); const cursor = useRef<string | undefined>(undefined);
  const actor = useRef<string | undefined>(undefined); const pending = useRef<AbortController | undefined>(undefined);
  const epoch = useRef(0); const alive = useRef(false);
  const feedback = useRef<HTMLDivElement>(null); const paging = useRef<HTMLDivElement>(null);
  const load = useCallback(async (criteria: Criteria, after?: string, kind: 'use' | 'retry' | 'reconnect' = 'use', restoring?: SearchReturn) => {
    // Move focus before a paging control retires. Reads must not interrupt an
    // edited filter or reclaim focus later when an asynchronous result arrives.
    if (paging.current?.contains(document.activeElement)) {
      feedback.current?.focus({ preventScroll: true });
      feedback.current?.scrollIntoView?.({ block: 'center', behavior: 'instant' });
    }
    pending.current?.abort(); const controller = new AbortController(); pending.current = controller;
    const ticket = ++epoch.current; setBusy(true); setReturning(Boolean(restoring)); setPage(undefined); setNotice(undefined); setAcknowledged(false); setReturnNotice(undefined);
    if (restoring) returnFocus.current = restoring.card;
    const started = performance.now(); activityEvent('search_read', kind);
    try {
      const result = await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(before)) throw new Error('Current account unavailable');
        if (restoring && restoring.actor !== before.id.toLowerCase()) throw new ChangedSearchAccount();
        if (actor.current && actor.current !== before.id.toLowerCase()) throw new ChangedSearchAccount();
        const query = new URLSearchParams(criteria); if (after) query.set('after', after);
        const response = await workRequest<unknown>(`/search?${query.toString()}`, { signal,
          headers: { 'X-StrataAI-Expected-Actor': before.id.toLowerCase() } });
        const afterProfile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(afterProfile) || before.id.toLowerCase() !== afterProfile.id.toLowerCase())
          throw new ChangedSearchAccount();
        return { actor: afterProfile.id.toLowerCase(), page: { ...parseSearchPage(response, afterProfile.id, after), locale: afterProfile.locale, timezone: afterProfile.timezone } };
      }, controller.signal);
      if (!alive.current || ticket !== epoch.current || controller.signal.aborted) return;
      const freshAcknowledgment = acknowledgments.current.consume(result.page.interaction);
      activityResult('search_read', true, started);
      actor.current = result.actor; applied.current = criteria; cursor.current = after; setPage(result.page);
      if (restoring) {
        setDraft(restoring.draft); setRestore(undefined); initialReturn.current = undefined;
        forgetSearchReturn(organizationId);
        setReturnNotice('Search refreshed from the first page.');
        returnFocus.current = restoring.card;
      }
      setAcknowledged(freshAcknowledgment);
    } catch (reason) {
      if (!alive.current || ticket !== epoch.current) return;
      activityResult('search_read', false, started);
      if (!(reason instanceof WorkRequestError) && !(reason instanceof ChangedSearchAccount) && !(reason instanceof ChangedSearchInteractionActor)) activityEvent('search_read', 'exception');
      setPage(undefined);
      if (reason instanceof ChangedSearchAccount || reason instanceof ChangedSearchInteractionActor || reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) {
        actor.current = undefined; cursor.current = undefined; applied.current = empty(); setDraft(empty());
        acknowledgments.current.clear();
        setRestore(undefined); initialReturn.current = undefined; forgetSearchReturn(organizationId);
      }
      setNotice('Search is unavailable. Check your account and access, then search again.');
    } finally {
      if (alive.current && ticket === epoch.current) { pending.current = undefined; setBusy(false); setReturning(false); }
    }
  }, [organizationId]);
  useEffect(() => {
    alive.current = true;
    activityEvent('search_disclosure', 'open');
    // Restore criteria only after both fresh account reads and the authorized
    // response pass. Restart traversal; never reuse a result snapshot/cursor.
    const saved = initialReturn.current;
    if (!saved) forgetSearchReturn();
    if (saved) { returnFocusAllowed.current = true; void load(saved.applied, undefined, 'use', saved); }
    const releaseFocus = () => { returnFocusAllowed.current = false; };
    document.addEventListener('pointerdown', releaseFocus, true); document.addEventListener('keydown', releaseFocus, true);
    const refresh = () => { if (!pending.current && document.visibilityState !== 'hidden' && actor.current) void load(applied.current, cursor.current); };
    const online = () => { if (!pending.current && document.visibilityState !== 'hidden' && actor.current) void load(applied.current, cursor.current, 'reconnect'); };
    window.addEventListener('focus', refresh); window.addEventListener('online', online);
    document.addEventListener('visibilitychange', refresh); const interval = setInterval(refresh, 10_000);
    return () => { alive.current = false; epoch.current++; pending.current?.abort(); clearInterval(interval);
      document.removeEventListener('pointerdown', releaseFocus, true); document.removeEventListener('keydown', releaseFocus, true);
      window.removeEventListener('focus', refresh); window.removeEventListener('online', online); document.removeEventListener('visibilitychange', refresh); };
  }, [load]);
  useEffect(() => {
    if (busy || returning || !page && !notice || !returnFocus.current) return;
    const card = returnFocus.current; returnFocus.current = undefined;
    if (returnFocusAllowed.current && (document.activeElement === document.body || document.activeElement === feedback.current))
      focusBoardReturn(links.current.get(card) ?? feedback.current);
    returnFocusAllowed.current = false;
  }, [busy, returning, page, notice]);
  const searchedWithFilters = Boolean(applied.current.q || applied.current.label || applied.current.member);
  const unappliedFilters = draft.q.trim() !== applied.current.q || draft.label.trim() !== applied.current.label
    || draft.member.trim() !== applied.current.member || draft.match !== applied.current.match || draft.scope !== applied.current.scope;
  return <Stack spacing={2} component="section" aria-label="Global Card search"
    sx={workExperienceEnabled ? { '& .MuiButton-root': { textTransform: 'none' } } : undefined}>
    <PageHeader title="Search Cards" variant="h5" description="Search across your accessible Organizations and Boards." />
    {!page && !busy && !notice && <Typography>Choose filters, then select Search. Leave filters empty to browse Cards you can access.</Typography>}
    <Stack component="form" spacing={2} useFlexGap={workExperienceEnabled}
      sx={{ '& .MuiInputBase-root .MuiSelect-select.MuiSelect-outlined.MuiInputBase-input': { whiteSpace: 'normal', overflowWrap: 'anywhere',
        paddingRight: 'calc(1.5em + 14px)' }, ...(workExperienceEnabled ? { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 16rem), 1fr))',
        alignItems: 'start', p: 2, bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: 2,
        '& .MuiFormControl-root': { minWidth: 0 }, '& .MuiButton-root': { minHeight: 56, textTransform: 'none' } } : {}) }}
      onSubmit={event => { event.preventDefault(); actor.current = undefined; setRestore(undefined); initialReturn.current = undefined; forgetSearchReturn(organizationId); returnFocusAllowed.current = false;
      void load({ ...draft, q: draft.q.trim(), label: draft.label.trim(), member: draft.member.trim() }); }}>
      <TextField disabled={returning} label="Card text" value={draft.q} onChange={e => setDraft({ ...draft, q: e.target.value })} slotProps={{ htmlInput: { maxLength: 160 } }} />
      <TextField disabled={returning} label="Label name" value={draft.label} onChange={e => setDraft({ ...draft, label: e.target.value })} slotProps={{ htmlInput: { maxLength: 160 } }} />
      <TextField disabled={returning} label="Member name" value={draft.member} onChange={e => setDraft({ ...draft, member: e.target.value })} slotProps={{ htmlInput: { maxLength: 160 } }} />
      <TextField disabled={returning} select label="Match criteria" value={draft.match} onChange={e => setDraft({ ...draft, match: e.target.value as Criteria['match'] })}>
        <MenuItem value="all">Match all</MenuItem><MenuItem value="any">Match any</MenuItem>
      </TextField>
      <TextField disabled={returning} select label="Search scope" value={draft.scope} onChange={e => setDraft({ ...draft, scope: e.target.value as Criteria['scope'] })}>
        <MenuItem value="active">Active work</MenuItem><MenuItem value="archived">Archived work</MenuItem>
      </TextField>
      <Button type="submit" variant="contained" disabled={busy || returning}>Search</Button>
    </Stack>
    <Stack ref={feedback} role="status" aria-live="polite" tabIndex={-1}
      sx={{ '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main', outlineOffset: 4, borderRadius: 1 } }}>{busy && <Typography>Searching…</Typography>}
      {notice && <Alert severity="warning">{notice}</Alert>}
      {page && <Typography>{page.items.length} result{page.items.length === 1 ? '' : 's'} on this page.{page.nextCursor ? ' More work can be searched.' : ' Search complete.'}</Typography>}
      {acknowledged && <Typography>Search acknowledged.</Typography>}
      {returnNotice && <Typography>{returnNotice}</Typography>}
    </Stack>
    {restore && !busy && notice && <Button onClick={() => { returnFocusAllowed.current = false; feedback.current?.focus({ preventScroll: true });
      void load(restore.applied, undefined, 'retry', restore); }}>Retry previous search</Button>}
    {page && !busy && !notice && unappliedFilters && <Typography role="note" aria-label="Unapplied Search filters" aria-live="polite">
      Filter changes are not applied. Select Search to update results. Paging and Refresh results still use the last search.
    </Typography>}
    {page && !busy && !notice && page.items.length === 0 && <Paper component="section" aria-label="Search guidance" variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={1}>
        <Typography component="h3" variant="h6">{page.nextCursor ? 'Keep searching' : 'No matches on this page'}</Typography>
        <Typography>{page.nextCursor
          ? 'Search is not finished. Select Next search page to continue checking work you can access.'
          : `This search has reached the last page. ${searchedWithFilters ? 'Try fewer filters, or choose' : 'Choose'} ${applied.current.scope === 'archived' ? 'Active work in Search scope for current Cards.' : 'Archived work in Search scope for archived Cards.'}`}</Typography>
      </Stack>
    </Paper>}
    {page?.items.map(item => <Paper key={item.id} variant="outlined" sx={{ p: 2, ...(workExperienceEnabled && { borderRadius: 2, minWidth: 0 }) }}>
      <Typography component={Link} to={`/app/${item.organizationId}/boards/${item.boardId}/cards/${item.id}`} state={{ cardOverlay: true }}
        ref={(node: HTMLAnchorElement | null) => { if (node) links.current.set(item.id, node); else links.current.delete(item.id); }}
        onClick={event => {
          if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
          if (organizationId && actor.current) retainSearchReturn({ organization: organizationId, actor: actor.current, entry: location.key,
            card: item.id, draft, applied: applied.current });
        }}
        sx={{ scrollMarginBlock: '8px', display: 'inline-block', ...(workExperienceEnabled ? { fontWeight: 600, color: 'primary.main', overflowWrap: 'anywhere', mb: 0.5,
          '&:focus-visible': { outline: '3px solid', outlineColor: 'primary.main', outlineOffset: 2 } } : {}) }}>{item.title}</Typography>
      <Typography variant={workExperienceEnabled ? 'body2' : undefined} color={workExperienceEnabled ? 'text.secondary' : undefined}>{item.boardName} / {item.listName}</Typography>
      <Typography variant={workExperienceEnabled ? 'body2' : undefined}>Labels: {item.labels.join(', ') || 'None'}{item.moreLabels ? ' (more on Card)' : ''}</Typography>
      <Typography variant={workExperienceEnabled ? 'body2' : undefined}>Members: {item.members.join(', ') || 'None'}{item.moreMembers ? ' (more on Card)' : ''}</Typography>
      <Typography variant={workExperienceEnabled ? 'body2' : undefined}>{item.dueAt ? `Due ${item.dueHasTime
        ? formatUserDateTime(item.dueAt, { locale: page.locale, timezone: item.boardDateTimezone ?? page.timezone }) ?? 'Date unavailable'
        : formatCardDate(item.dueAt, false, page.locale, item.boardDateTimezone ?? page.timezone)}${item.dueComplete ? ' — completed' : ''}` : 'No deadline'}</Typography>
    </Paper>)}
    <Stack ref={paging} spacing={2} direction={workExperienceEnabled ? 'row' : undefined} useFlexGap={workExperienceEnabled}
      sx={workExperienceEnabled ? { flexWrap: 'wrap', justifyContent: 'flex-end' } : undefined}>
      {page?.nextCursor && <Button disabled={busy} onClick={() => void load(applied.current, page.nextCursor!)}>Next search page</Button>}
      {page && <Button disabled={busy} onClick={() => void load(applied.current, cursor.current, 'retry')}>Refresh results</Button>}
    </Stack>
  </Stack>;
}
