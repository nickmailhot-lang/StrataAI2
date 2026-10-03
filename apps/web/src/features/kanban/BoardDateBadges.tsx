import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Alert, Box, Button, Chip, Typography } from '@mui/material';
import { CalendarToday, CheckCircle, Schedule, Warning } from '@mui/icons-material';
import { boundedWorkRead, workRequest, type BoardSnapshot, type WorkCard } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { cardDates, cardDueState, dateTimezone, nextCardDateWake, type DueState } from './cardDates';

type DateView = { timezone?: string; now: number; failed: boolean };
const DateContext = createContext<DateView>({ now: 0, failed: true });
const labels: Record<DueState, string> = { UPCOMING: 'Upcoming', DUE_SOON: 'Due soon', DUE_TODAY: 'Due today', OVERDUE: 'Overdue', COMPLETE: 'Complete' };

export function BoardDateProvider({ snapshot, unavailable, onRevalidate, children }: {
  snapshot: BoardSnapshot; unavailable: boolean; onRevalidate: () => void; children: ReactNode;
}) {
  const cards = useMemo(() => snapshot.lists.flatMap(column => column.cards).filter(card => !!card.dueAt), [snapshot.lists]);
  const enabled = cards.length > 0;
  const [viewerZone, setViewerZone] = useState<string>();
  const [failed, setFailed] = useState(false), [attempt, setAttempt] = useState(0), [now, setNow] = useState(Date.now);
  const subject = useRef<string | undefined>(undefined), refresh = useRef(onRevalidate); refresh.current = onRevalidate;
  useEffect(() => {
    setViewerZone(undefined); setFailed(false);
    if (!enabled || unavailable) return;
    let active = true, reading = false, admissionRequired = false;
    let controller: AbortController | undefined;
    const load = async () => {
      if (!active || reading || admissionRequired || document.visibilityState === 'hidden') return;
      reading = true; controller = new AbortController();
      try {
        const value = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
        if (!active || controller.signal.aborted) return;
        if (!isNotificationProfile(value)) throw new Error('Invalid date viewer');
        dateTimezone(value.timezone);
        if (subject.current && subject.current !== value.id) {
          subject.current = value.id; admissionRequired = true;
          setViewerZone(undefined); setFailed(true); refresh.current(); return;
        }
        subject.current = value.id; setViewerZone(value.timezone); setFailed(false); setNow(Date.now());
      } catch {
        if (active) { setViewerZone(undefined); setFailed(true); }
      } finally { reading = false; }
    };
    void load();
    const check = () => { void load(); };
    const timer = setInterval(check, 30_000);
    window.addEventListener('focus', check); document.addEventListener('visibilitychange', check);
    return () => { active = false; controller?.abort(); clearInterval(timer); window.removeEventListener('focus', check); document.removeEventListener('visibilitychange', check); };
  }, [enabled, unavailable, attempt, snapshot.board.organizationId, snapshot.board.id]);
  let timezone: string | undefined, policyInvalid = false;
  if (viewerZone && !unavailable) {
    try { timezone = dateTimezone(snapshot.board.dateTimezoneOverride ?? viewerZone); } catch { policyInvalid = true; }
  }
  useEffect(() => {
    if (!timezone) return;
    const timer = setTimeout(() => setNow(Date.now()), nextCardDateWake(cards, timezone, now));
    return () => clearTimeout(timer);
  }, [cards, timezone, now]);
  const value = useMemo(() => ({ timezone, now, failed: unavailable || failed || policyInvalid }), [timezone, now, unavailable, failed, policyInvalid]);
  return <DateContext.Provider value={value}>
    {enabled && !unavailable && (failed || policyInvalid) && <Alert severity="warning">Due statuses are unavailable. <Button onClick={() => { refresh.current(); setAttempt(value => value + 1); }}>Check date display</Button></Alert>}
    {enabled && !unavailable && !failed && !policyInvalid && !viewerZone && <Typography role="status">Loading due statuses…</Typography>}
    {children}
  </DateContext.Provider>;
}

export function cardDueDescriptionId(cardId: string): string { return `card-due-${cardId}`; }
export function CardDueBadge({ card }: { card: WorkCard }) {
  const view = useContext(DateContext);
  if (!card.dueAt) return null;
  const id = cardDueDescriptionId(card.id);
  if (!view.timezone) return <Box component="span" id={id} sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clipPath: 'inset(50%)' }}>{view.failed ? 'Due status unavailable.' : 'Loading due status.'}</Box>;
  try {
    const state = cardDueState(cardDates(card), view.timezone, view.now);
    if (!state) return null;
    const icon = state === 'COMPLETE' ? <CheckCircle /> : state === 'OVERDUE' ? <Warning /> : state === 'UPCOMING' ? <CalendarToday /> : <Schedule />;
    return <Chip id={id} size="small" icon={icon} label={labels[state]} color={state === 'OVERDUE' ? 'error' : state === 'COMPLETE' ? 'success' : 'default'} sx={{ mt: 1 }} />;
  } catch { return <Chip id={id} size="small" icon={<Warning />} label="Due status unavailable" sx={{ mt: 1 }} />; }
}
