import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Alert, Box, Button } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationUuid } from '../notifications/notificationInbox';
import { watchIdentity } from '../auth/identityLive';
import { readRetentionPolicies } from './retentionPolicyClient';
import { RetentionSourceControl } from './RetentionSourceControl';
import { forgetRetentionSourceClassifications, forgetRetentionSourceClassificationForSource,
  forgetRetentionSourceClassificationsForCard, hasRetentionSourceRecovery } from './retentionSourceRecovery';
import type { RetentionSourceKind } from './retentionSourceClient';

export type SourceRetentionEntryProps = {
  organizationId: string; boardId: string; cardId: string; cardVersion: number;
  kind: RetentionSourceKind; recordId: string; sourceVersion: number;
  canAdminister: boolean; unavailable: boolean; expectedActor?: string; onRefresh: () => void;
};
type Admission = { actorId: string; version: number; state: 'admitted' | 'uncertain' | 'denied' };
const names = { card: 'Card', cardComment: 'Comment', attachment: 'Attachment' };
const maximumId = 'ffffffff-ffff-ffff-ffff-ffffffffffff';
export function SourceRetentionEntry(props: SourceRetentionEntryProps) {
  return <Entry key={`${props.organizationId}/${props.boardId}/${props.kind}/${props.recordId}/${props.expectedActor ?? 'current'}`} {...props} />;
}
function Entry(props: SourceRetentionEntryProps) {
  const [open, setOpen] = useState(false), [busy, setBusy] = useState(false);
  const [admission, setAdmission] = useState<Admission>(), [notice, setNotice] = useState<string>();
  const region = useId(), active = useRef<AbortController | undefined>(undefined), epoch = useRef(0), mounted = useRef(true);
  const callbacks = useRef(props); callbacks.current = props;
  const admittedActor = useRef<string | undefined>(undefined); admittedActor.current = admission?.actorId;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; ++epoch.current; active.current?.abort(); }; }, []);
  const admit = useCallback(async () => {
    if (!callbacks.current.canAdminister || callbacks.current.unavailable) return;
    const controller = new AbortController(), attempt = ++epoch.current; active.current?.abort(); active.current = controller;
    setBusy(true); setNotice(undefined); setAdmission(value => value && { ...value, state: 'uncertain' });
    let organizationDenied = false;
    let subject = callbacks.current.expectedActor ?? admittedActor.current;
    try {
      const scope = callbacks.current;
      const result = await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(before) || subject && before.id.toLowerCase() !== subject.toLowerCase()) throw new WorkRequestError(401, null);
        subject = before.id.toLowerCase();
        // Reuse the existing Owner/Admin gate without retaining another policy
        // body. This does not grant source permission; each command owns that.
        let directory;
        try { directory = await readRetentionPolicies(scope.organizationId, signal, subject, maximumId); }
        catch (error) {
          organizationDenied = error instanceof WorkRequestError && [401, 403, 404].includes(error.status); throw error;
        }
        if (directory.page.items.length) throw new WorkRequestError(503, null);
        const value = await workRequest<unknown>(`/boards/${encodeURIComponent(scope.boardId)}`, { signal });
        if (!value || typeof value !== 'object') throw new WorkRequestError(503, null);
        const row = value as Record<string, unknown>, board = row.board as Record<string, unknown> | undefined;
        const access = row.access as Record<string, unknown> | undefined;
        if (!board || !access || !notificationUuid(board.id) || board.id.toLowerCase() !== scope.boardId.toLowerCase()
          || !notificationUuid(board.organizationId) || board.organizationId.toLowerCase() !== scope.organizationId.toLowerCase()
          || typeof access.canView !== 'boolean' || typeof access.canAdminister !== 'boolean'
          || !['active', 'archived'].includes(board.lifecycleState as string)) throw new WorkRequestError(503, null);
        if (!access.canView || !access.canAdminister) throw new WorkRequestError(403, null);
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id.toLowerCase() !== directory.actorId) throw new WorkRequestError(401, null);
        return { actorId: directory.actorId, version: profile.version };
      }, controller.signal);
      if (mounted.current && attempt === epoch.current && !callbacks.current.unavailable && callbacks.current.canAdminister)
        setAdmission({ ...result, state: 'admitted' });
    } catch (error) {
      if (!mounted.current || attempt !== epoch.current) return;
      const denied = error instanceof WorkRequestError && [401, 403, 404].includes(error.status);
      setAdmission(value => error instanceof WorkRequestError && error.status === 401 ? undefined : value && { ...value, state: denied ? 'denied' : 'uncertain' });
      if (denied && subject) {
        if (organizationDenied || error instanceof WorkRequestError && error.status === 401)
          forgetRetentionSourceClassifications(subject, props.organizationId);
        else forgetRetentionSourceClassificationForSource(subject, props.organizationId, props.kind, props.recordId);
      }
      setNotice(denied ? 'Retention classification access is unavailable.' : 'Retention access could not be confirmed. Original work is retained; retry the access check.');
      if (denied) callbacks.current.onRefresh();
    } finally {
      if (mounted.current && attempt === epoch.current) { active.current = undefined; setBusy(false); }
    }
  }, [props.organizationId, props.kind, props.recordId]);
  useEffect(() => {
    if (!props.canAdminister || props.unavailable) {
      ++epoch.current; active.current?.abort(); setBusy(false);
      if (!props.canAdminister && !props.unavailable && admittedActor.current)
        forgetRetentionSourceClassificationsForCard(admittedActor.current, props.organizationId, props.cardId);
      // Keep the Card boundary mounted when its administration rights disappear.
      // A fresh Organization check can distinguish loss of all classification
      // authority from loss on this Board, without reading a private source body.
      if (!props.canAdminister && !props.unavailable && props.kind === 'card' && hasRetentionSourceRecovery(props.organizationId)) {
        const controller = new AbortController(), attempt = epoch.current;
        active.current = controller; let subject = props.expectedActor ?? admittedActor.current;
        void boundedWorkRead(async signal => {
          const profile = await workRequest<unknown>('/me', { signal });
          if (!isNotificationProfile(profile) || subject && profile.id.toLowerCase() !== subject.toLowerCase()) throw new WorkRequestError(401, null);
          subject = profile.id.toLowerCase();
          await readRetentionPolicies(props.organizationId, signal, subject, maximumId);
          if (mounted.current && epoch.current === attempt && !callbacks.current.canAdminister && !callbacks.current.unavailable)
            forgetRetentionSourceClassificationsForCard(subject, props.organizationId, props.cardId);
        }, controller.signal).catch(error => {
          if (mounted.current && epoch.current === attempt && subject && error instanceof WorkRequestError && [401, 403, 404].includes(error.status))
            forgetRetentionSourceClassifications(subject, props.organizationId);
        }).finally(() => { if (active.current === controller) active.current = undefined; });
      }
      setAdmission(value => value && { ...value, state: props.canAdminister ? 'uncertain' : 'denied' });
    } else if (open) void admit();
  }, [props.canAdminister, props.unavailable, props.cardVersion, props.sourceVersion, props.expectedActor, open, admit, props.organizationId, props.cardId, props.kind, props.recordId]);
  useEffect(() => {
    if (!admission?.actorId) return;
    return watchIdentity({ subject: admission.actorId, initialVersion: admission.version, isProfile: isNotificationProfile,
      invalidate: () => {
        if (!mounted.current) return;
        setAdmission(value => value && { ...value, state: 'uncertain' });
        callbacks.current.onRefresh(); void admit();
      } });
  }, [admission?.actorId, admission?.version, admit]);
  const authority = props.unavailable ? 'uncertain'
    : !props.canAdminister || admission?.state === 'denied' ? 'denied'
    : admission?.state !== 'admitted' ? 'uncertain' : 'admitted';
  if (!props.canAdminister && !open && !admission) return null;
  return <Box sx={{ minWidth: 0 }}>
    <Button aria-expanded={open} aria-controls={region} disabled={!props.canAdminister && !open}
      onClick={() => setOpen(value => !value)}>{open ? 'Hide retention classification' : `Retention classification for this ${names[props.kind]}`}</Button>
    <Box id={region} hidden={!open}>
      {notice && <Alert severity="warning">{notice}</Alert>}
      {busy && <Alert severity="info" role="status">Checking current retention access…</Alert>}
      {!busy && authority !== 'admitted' && props.canAdminister && !props.unavailable
        && <Button onClick={() => void admit()}>Retry retention access</Button>}
      {admission && <RetentionSourceControl organizationId={props.organizationId} actorId={admission.actorId}
        boardId={props.boardId} cardId={props.cardId} cardVersion={props.cardVersion} kind={props.kind}
        recordId={props.recordId} sourceVersion={props.sourceVersion} authority={authority}
        accessLost={() => { setAdmission(value => value && { ...value, state: 'denied' }); callbacks.current.onRefresh(); }}
        completed={() => { /* The immutable classification does not revise the operational Card. */ }} />}
    </Box>
  </Box>;
}
