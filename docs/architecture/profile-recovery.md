# Profile recovery

## Authoritative profile reads

`GET /me` now uses the Application identity service and existing identity
transaction, with account locking and initial/final actor admission. Cookie
authentication still supplies identity claims, but no longer caches a profile
for this endpoint to disclose without those checks. The dedicated profile read
fetches current account fields without reading or exposing the event journal.
Its public response shape and expected-account command protocol are unchanged.

The new `/me` case fails against the original endpoint because it performs zero
transactional actor checks; the two existing sync cases pass. After repair, all
23 selected profile-related API-host cases pass, including the three protected
read cases, replay and concurrency checks. Denial returns 401 without private
account fields or a cookie, and preserves account/event state. The locked Release
API/test build completes with zero warnings/errors.

Two complete local Production/PostgreSQL probes also pass against the repaired
compiled API on 2026-10-07. Each holds its fixture account row, observes the actual
restricted API query waiting for that lock, then revokes or expires the original
session before release. The read returns neutral `session_unavailable`, discloses
no protected fields/cookie, and leaves exact user/session/audit/stream/event/profile
receipt snapshots unchanged after the controlled session change. Restoring the
fixture session recovers the exact prior profile. The disposable API is removed;
the original services are preserved. This is controlled expiry before initial
transaction admission, not elapsed expiry during final serialization.

The mandatory exact-image identity command fixture now includes both profile-read
withdrawal cases alongside its existing command/sync checks, and verifies safe
refusals, unchanged protected state and independent-session access. Bash syntax
and diff checks pass. Full native fixture/current retained-image execution and
complete PRD acceptance remain required; these scoped local results do not close
PRD-02.

## Expected account during commands

The profile form now sends `X-StrataAI-Expected-User` with the account ID whose
draft or confirmation is being submitted. Profile saves, sign-out, deactivation
and mention-handle claims retain that original ID on retries. In particular,
deactivation stores it beside the immutable retry key before protected profile
details are cleared after an uncertain response.

The corresponding API endpoints compare a supplied ID with the authenticated
principal before invoking the identity command service. A changed active cookie
subject or malformed supplied header receives neutral HTTP 401
`session_unavailable`, without deleting the replacement account's cookie or
executing the command. The header is an intent fence, not authentication or a
permission grant. Existing callers may omit it and continue to address their
current authenticated account. Anonymous logout/deactivation receipt recovery
still requires the original opaque session and existing receipt capability;
the header cannot authorize that recovery by itself.

This closes a stale-tab defect: previously the form checked the response subject
only after a profile write. Another tab could replace the shared cookie, causing
the old draft to update the newly signed-in account, or stale deactivation/sign-out
to affect it. Three API regressions reproduce the original successful operations
where refusal is required. After the correction, eight expected-account API cases
pass (four changed-subject endpoints and four malformed-header cases), together
with two existing profile replay/actor/session cases. The locked Release build
has zero warnings/errors. All 37 selected profile/deactivation/handle component
cases, web/browser typechecks, scoped lint and production build pass.

Seven native browser scenarios pass in one 3.3-minute invocation against the frozen
compiled Production API, restricted schema-110 PostgreSQL and production bundle
behind current Nginx: desktop/phone stale-profile cookie switching, desktop/phone
lost deactivation acknowledgments, original-session logout receipt recovery, lost
profile-save acknowledgment and two-browser persisted conflict recovery. Cookie
switching preserves both accounts' exact profile/event snapshots, sends the original
expected ID, returns 401 without cookie deletion and clears the stale draft from
the page. All four cookie-switch accounts remain at version 1 in PostgreSQL.
Original receipt keys and once-only effects remain intact in the existing scenarios.

The new browser scenarios simulate recovery-network loss only; authentication,
account writes and refusals are real. These local compiled hosts/bundles in cached
runtime images do not establish current retained-image, separate Worker/mail or
complete PRD acceptance, and scenario durations are not performance measurements.
Temporary API/web containers are retired after execution. The mandatory API suite
and full build-once browser suite include the new regressions.

### Executed four-command cookie-switch matrix

The native expected-account scenario now covers all four fenced commands at both
1280px and 390px: profile save, sign-out, confirmed deactivation and handle claim.
The complete eight-case invocation passes with exit code 0 in 3.6 minutes against
the same frozen compiled Production API, restricted PostgreSQL and production
bundle. Each actual request carries the original account ID, receives neutral
401 without cookie deletion and removes the stale view without claiming account
deactivation. Both accounts remain authenticated and their exact profile/event
snapshots are unchanged. Handle cases additionally preserve both exact settings.

The handle case switches the shared cookie after the server has completed a real
original-account preflight, before its reply allows the mutation to be sent. This
verifies that a successful client preflight cannot replace the server intent fence.
Authentication and mutation responses remain actual server responses. Profile
recovery-network loss alone is simulated. All sixteen fixture accounts remain at
version 1 in PostgreSQL. Browser typechecking and diff checks pass; no production
policy or code changes are needed for this coverage extension. It does not certify
current retained-image, full concurrency, mail/Worker or complete PRD acceptance.

## Executed local Production recovery

The existing native account fixtures pass against the separate schema-110
Production API and restricted PostgreSQL role at compiled source revision
`6044227e`, using Vite and ordinary registration/sign-in. Desktop and phone
keyboard deactivation lose the first actual 204 acknowledgment, preserve the
original issued session cookie, and recover the same command key exactly once.
The account is denied afterward. A PostgreSQL catalog check confirms that both
test accounts are `DEACTIVATED` at version 2, each with exactly one canonical
`USER_DEACTIVATED` event, one audit record and one issuer-authority source carrying
the original actor and version. Original-session logout acknowledgment recovery
also passes with the same key and final cookie withdrawal.

The desktop/phone two-page profile scenario passes actual persisted saves,
canonical `USER_PROFILE_UPDATED` event recovery, UTC timestamp presentation,
dirty-draft preservation, disabled stale saving, explicit discard/load-latest,
merged save recovery in the other page and automatic logout propagation without
manual document reload. Both local invocations completed successfully: two
deactivation cases in 11.3 seconds and logout/profile cases in 15.3 seconds.
These are scenario durations, not mutation latency or performance benchmarks.
The persisted merged-profile account is active at version 3 with timezone `UTC`
and exactly two canonical `USER_PROFILE_UPDATED` events.

The framework API build and local browser server do not certify retained
release-image identity. Complete current CI, every date-display consumer,
production policy/mail/expiry/privacy cases and remaining PRD acceptance still
need their own evidence. PRD-02 and PRD-60 remain open.

The MUI profile form reads the authoritative profile and identity-event cursor through `/me/sync` every ten seconds while visible and also on focus, reconnection and visibility recovery. Continuation pages follow immediately with at most 100 events per response. One read is active at a time. A fifteen-second deadline covers both transport and response-body parsing; timeout aborts the request, releases the read slot and fences an ignored-abort response. Unmount/reload removes listeners and timers and aborts the owned request. The event envelope is validated before its cursor advances; see [identity-events.md](identity-events.md).

Recovery reads require the profile fields and a positive safe-integer version. Older/equal versions cannot replace a newer profile or draft. Starting a save or sign-out changes the read epoch, so an earlier read cannot overwrite its acknowledgment. Session denial clears the view and redirects to sign-in; a changed subject also requires sign-in rather than retaining the previous draft.

A clean form adopts newer name/avatar/locale/timezone values automatically. A dirty form preserves its exact draft and original version, updates the authoritative summary and announces that the profile changed elsewhere. Saving stays disabled until explicit discard-and-load-latest. Read failure preserves the form and exposes an accessible retry notice; periodic/focus recovery continues. The [identity SignalR stream](identity-realtime.md) invalidates this authoritative recovery when committed events arrive; it does not replace drafts directly.

Account status uses canonical API strings (`PENDING_VERIFICATION`, `ACTIVE`, `SUSPENDED`, `DEACTIVATED`) rather than numeric enum ordinals. This matches the profile contract and provides an explicit lifecycle label.

Profile save and sign-out also use a fifteen-second deadline. Save bounds both transport and response-body parsing, while sign-out needs only the response status. Timeout aborts the owned request and releases the form, even when transport ignores abort. No mutation is automatically retried: a timed-out save may already have committed. The draft and its original version remain available, normal recovery can discover a newer profile, and an unchanged manual retry retains the same durable profile key. Changed input starts a new key and must satisfy the current version precondition. Sign-out also retains its key after an uncertain outcome; the restricted session receipt can acknowledge completed revocation without restoring authentication. See [identity command retries](identity-command-retries.md). The error describes an unconfirmed save rather than claiming the server rolled it back.

A successful save acknowledgment must satisfy the profile schema, match the submitted user and contain a strictly newer version. Invalid, unrelated or nonadvancing responses preserve the draft and cannot show success. Mutation ownership prevents duplicate submissions before React renders the busy state. Unmount aborts the owned command, clears its timer and fences all late state changes/navigation. Session denial and confirmed sign-out clear the profile before navigating. Focused tests cover transport/body stalls, explicit retry, late success, invalid acknowledgments and unmount cancellation.

Account-created and last-updated timestamps render through the shared `formatUserDateTime` helper using the saved locale and timezone, with semantic `<time>` elements and explicit zone labels. Browser-local timezone is never a fallback. Unsupported historic settings show an unavailable-date message until corrected. Automatic clean-form preference recovery also reformats these dates.

Registration and profile edits share regional-locale and timezone validation. Valid Windows timezone IDs map through the .NET timezone database to their default IANA counterpart for browser interoperability; UTC remains UTC. New writes persist canonical IDs, and profile responses normalize valid historic Windows IDs without rewriting history/version. Invalid inputs are rejected before account creation. Existing IANA choices, including America/Vancouver, remain intact.

Component checks cover periodic/focus recovery, dirty drafts, background denial, an abort-ignoring timeout and late results after timeout/save. Formatter checks cover midnight boundaries, daylight saving changes and invalid values. API tests cover invalid registration preferences and Windows/IANA interoperability. The required account browser scenario uses desktop and phone-sized profile views, automatic conflict discovery and clean-form recovery, saved-timezone timestamp display, explicit draft discard, and automatic logout recovery without manual page reloads. These checks do not complete durable identity idempotency/events, timezone integration for future date features, ownership continuity or all PRD-02/60 criteria.
