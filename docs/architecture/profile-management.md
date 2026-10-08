# Account profile management (PRD-02)

The MUI profile screen loads the authenticated account from `GET /me` and saves
display name, avatar URL, locale and timezone through `PATCH /me`. Email and
account status remain read-only. The screen displays the returned server state,
preserves edits on failed saves, supports retry/discard, and redirects to sign-in
when the session expires. Failed sign-out leaves the user on their profile with
a retry message.

`GET /me` reads the current profile through the identity Application service,
inside the existing identity transaction. The account lock and initial session
check precede that read, and a final actor check precedes disclosure. It no longer
returns a snapshot cached during cookie authentication. Its profile response
shape is unchanged; this read does not query the event journal or publish events.
See [executed profile-read evidence](profile-recovery.md#authoritative-profile-reads).

Profile updates require a nonempty display name of at most 120 characters, a
regional locale, and a timezone recognized by the runtime. Avatar URLs must use
HTTPS without embedded credentials and be at most 2,048 characters. An empty
avatar URL removes it; an omitted/null URL preserves it. The API stores the URL
without fetching it. Updates preserve identity and historical attribution and
emit the existing `USER_PROFILE_UPDATED` audit entry.

Verification: `ProfilePage.test.tsx` covers authoritative save responses,
network retry, server validation, discard, session expiration, and sign-out
failure. `scripts/ci/test-demo-auth.sh` covers persisted preferences, invalid
input rejection without mutation, avatar removal, and unauthenticated updates
(PRD-02-TC-01/03/04/06). CI runs the account lifecycle against the exact API image.

Profile writes require the positive `version` returned by `GET /me`. Missing or
invalid versions return HTTP 400 (`invalid_version`); stale versions return HTTP
409 (`version_conflict`). Both stores compare the version atomically while
writing, increment it only on success, and reject writes to deactivated accounts.
The UI preserves conflicted edits and offers an explicit discard-and-reload
action. Clients must reload and reconcile before resubmitting with a new version.

`test-profile-concurrency.sh` checks simultaneous saves (exactly one succeeds),
stale retries without overwrite, and recovery using the latest version. It runs
against both Demo and the PostgreSQL provider using the exact CI API image.
The CI-only Compose overlay enables disposable registration without verification
after checking secure production defaults. It is excluded from release bundles.
No schema change is needed: the existing user version column is used.

Password recovery screens are available at `/forgot-password` and
`/reset-password#token=...`. The request screen uses identical confirmation for
known/unknown accounts and never displays the Demo API's returned token. The
reset screen consumes a fragment token, immediately removes the fragment from
history, checks password confirmation, handles expired/single-use tokens, and
clears the token/passwords after success. Tokens in query strings are not accepted.
The API enforces password policy and revokes prior sessions on successful reset.
Component tests verify confirmation privacy, rate/network errors, token handling,
success and rejection; container browser tests cover the request and invalid-link
recovery. Demo API tests prove reset token reuse is rejected.

Production email delivery now uses the separate Worker, durable global identity
outbox and provider idempotency described in [identity-email.md](identity-email.md).
Verification/resend screens and a mobile keyboard test consume real queued fixture
links, including successful reset and old-session revocation. Deployment still
requires runtime keys, restricted database credentials and provider configuration.

Profile update delivery and reconnect recovery are implemented through the
account-bound SignalR and HTTP recovery contracts in [identity realtime
recovery](identity-realtime.md) and [profile recovery](profile-recovery.md).
The account browser fixture checks actual identity frames and two-client
recovery; the realtime guide records the tested historical revision. Those
results must not be treated as release evidence for a later commit.

PRD-02 remains open. Verify current account lifecycle, preference consumers,
accessibility and all remaining acceptance requirements against the current
release images before closure.

### Activity and personal star history date display

Board/Card activity and personal star history now use the shared
`formatUserDateTime` formatter. Both use the locale/timezone from the final
confirmed account read, include an explicit timezone label, and retain the
original UTC instant in their semantic time elements. Display formatting does
not alter stored events, history revisions or paging cursors.

Two regressions failed before this change because the rendered timestamps lacked
the zone label. They now check Honolulu's previous calendar day, a later Tokyo
preference on history refresh, and the same original microsecond timestamp.
All 21 focused history/formatter cases pass, as do web/browser TypeScript and
lint. Desktop/phone activity and star release fixtures now compare each relevant
caption and datetime attribute with the actual stored source and known account
preferences; their new assertions still require exact-image CI execution.

The search deadline fixture now verifies actual automatic recovery after an
independently signed-in second session updates the account timezone. It preserves
an unsent filter draft and the original applied query, without clicking Refresh
results or reloading the document. Both 1280px and 390px local Production cases
passed on 2026-10-07 (18.8 and 17.0 seconds; these are whole-scenario durations,
not latency benchmarks). The existing Board-policy precedence/change/clearing,
unchanged UTC instant, date-only presentation, keyboard Search focus and automated
WCAG-tagged checks also passed. Browser TypeScript and the isolated production
web build passed.

The first invocation failed because Vite returned HTML for `/search?...`; its
API-root proxy boundary now includes the query marker. The successful invocation
used the schema-110 restricted PostgreSQL API compiled at `6044227e` and current
Vite source with this repair. Backend `src` files are unchanged between that
compiled revision and `f5fc2efb`. The scoped actual HTTP/DB/browser result is not
an exact retained-release-image or full PRD acceptance claim.

On 2026-10-07, a current-source isolated Release API-test build completed with
zero warnings/errors. The search date-policy HTTP case passed, retaining timed
and date-only UTC deadlines across Board policy changes/clearing and excluding
private date/policy details from outsider results. Five profile retry cases also
passed: both /me route forms serialize same-key concurrent saves, preserve one
canonical event, reject collisions/stale edits, isolate actors and refuse replay
after logout; three invalid keys leave profile version/events unchanged.
These framework-host checks use Demo persistence. They strengthen their named
HTTP contracts and do not prove all Production/native/release acceptance.

Open Board/Card activity now also subscribes to the admitted account's identity
stream and recovers through the existing protected history read. Visible pages
check every ten seconds and on focus, online and visibility recovery. The current
history cursor remains selected. Signals received during a bounded read queue
one follow-up instead of aborting that read repeatedly. Each recovery still
validates the profile before and after the protected page. Denial clears history
and stops recovery until the parent supplies fresh admission; closing or
unmounting retires the subscription, timers/listeners and owned read.

An identity-delivery regression first failed on the absent subscription and now
passes a Honolulu-to-Tokyo update on an older page without resetting its cursor
or changing the source instant. The completed 19-case activity/identity-stream
suite also passes periodic fallback, queued signals, close/denial retirement,
original keyboard paging focus and background focus retention without stealing
an unrelated control. Web TypeScript/lint and browser TypeScript pass. The
desktop/phone activity fixture now changes the peer account's timezone through
the normal versioned profile endpoint and requires its already-open history to
recover the new caption without document reload or source changes. That new
native assertion remains pending exact-image execution. This does not close
AUTH-FR-010 or AC-AUTH-02-03.

Personal star history now uses the same identity-delivery and visible-page
periodic/focus/online/visibility recovery policy. It retains its selected revision
cursor and stable preference identity, queues one follow-up during a protected
read, and stops background reads after 401/403/404 until fresh parent admission.
The completed combined activity/star/identity suite passes all 33 cases, including
continuation-page timezone recovery, periodic fallback, queued signals, denial
and close retirement, background focus in a real MUI Dialog and deliberate focus
on another control during a delayed read. Web/browser TypeScript and lint pass.
The desktop/phone star fixture changes the account's timezone from its other
client and requires the already-open mirror history to recover with original
timestamps and revision count. Native execution remains pending. Search,
comments, invitations and other consumers still need their own full automatic
preference-recovery and release audit.

### Comment timezone display and remaining acceptance

AUTH-FR-010 applies to timestamp displays as well as Card date controls.
[Card comments](../../apps/web/src/features/kanban/CardCommentsControl.tsx) now
retain locale/timezone from the validated current account after the review read
and after mutation acknowledgment. Both reviewed and newly acknowledged comments
use the [shared date/time formatter](../../apps/web/src/features/auth/userDateTime.ts).
Formatting changes display only; immutable UTC timestamps and original command
keys, bodies and versions remain unchanged. Failed formatting displays an explicit
unavailable date rather than silently using the browser timezone. Account refusal
and cancellation retain the existing protected-state and late-response fences.

The [comment component fixtures](../../apps/web/src/features/kanban/CardCommentsControl.test.tsx)
verify Honolulu's previous calendar day and Tokyo's later time after preference
changes during lost-response recovery, with the original request and timestamp
unchanged. The desktop/phone [native comment fixture](../../tests/browser/card-comments.spec.ts)
registers real Honolulu preferences and checks both acknowledgment and reviewed
captions against the stored instant. Its execution remains pending in CI.
Notifications and activity history already use account preferences. Broader
preference-consumer and current-release acceptance remains required; these scoped
checks do not complete PRD-02.

`tests/browser/account.spec.ts` runs Chromium against the actual web/API release
images and PostgreSQL. It checks registration, sign-in through Nginx, profile
persistence, two-page stale-save recovery and logout/session revocation. Browser
traces and screenshots are retained on failure. Nginx and the Vite development
proxy forward the API's top-level routes as well as `/api`; `/app`, `/login` and
`/portal` remain SPA routes.

### Global search deadline display

Global search now stores the final validated account locale/timezone alongside
its admitted result page and formats deadline instants with the shared account
date/time formatter. Refresh replaces results and preferences together; refusal
or a new read removes both. Stored UTC deadlines and search criteria/cursors are
unchanged. Formatting failure displays an explicit unavailable date.

A component case requires Honolulu's previous day and Tokyo's later time after
a profile preference refresh, with the same UTC search result. Native desktop
and phone fixtures create an actual timed Card deadline and change the account
preference before refreshing search. Ten focused component cases, source
TypeScript and lint pass; native fixture execution remains pending CI. Search now carries canonical nullable `boardDateTimezone` from its authorized
Board read. Applying that optional policy in the browser remains the next step. This account-preference repair
does not establish full AUTH-FR-010/PRD-02 or PRD-16 acceptance.

The search document's `boardDateTimezone` comes from the canonical Board already
held by the authorized Work read, not from directory routing hints. No extra
connection, tenant context or database migration is introduced. A new API-host
case checks null policy, Honolulu, Tokyo and clearing; every fresh search must
reflect the admitted current policy while an outsider receives no private Card or
policy metadata. Compilation succeeds; native execution remains pending CI.
Browser policy precedence is still incomplete, so this contract increment does
not close the timezone acceptance gap.

The mandatory exact-image global-search fixture now requires the
`boardDateTimezone` field on every admitted result, checks initial null policy,
Honolulu, Tokyo and clearing through actual Board date-policy commands, and
requires private/no-store search responses. Its outsider checks require no private
Board/List names or policy strings. Existing 50+2 paging, cursor binding and
archived-parent checks remain required afterward. Bash syntax passes; native
execution is pending. The complete web run against unchanged browser source at
`b9f257e` completed successfully: 122 test files and 1,491 tests passed, with
process exit 0 (508.60 seconds). This verifies the account-preference display
increment across the web suite; backend/fixture coverage does not prove browser
Board-policy precedence or native release acceptance.

Search now validates the required nullable `boardDateTimezone` before admitting
each result. Invalid or missing policy rejects the page rather than using the
browser timezone. Display uses that Board policy when present and otherwise the
final admitted account timezone; account locale applies in both cases. A focused
component case checks Honolulu, a change to UTC, and clearing back to Tokyo with
the same stored UTC deadline. Parser cases cover valid/null policies and malformed
values. All 19 focused search tests pass. Desktop and phone native fixtures now
change and clear the actual Board policy, refresh results, and verify that the
stored deadline remains the same instant; their execution remains pending CI.

The full web suite against unchanged browser source at `ea1e37b` completed with
exit 0: 122 files and 1,493 tests passed in 498.85 seconds. This includes Board
timezone precedence, policy changes/clearing and account-preference fallback.
The native fixture repair in `3a1901c` reads `board.version` and the persisted
Card from `lists[].cards[]` in the canonical Board snapshot; there is no generic
Card GET route. Browser TypeScript passes for the repair. Native execution and
current release-image evidence remain pending, so these results do not close
PRD-02 or PRD-16.

Profile save acknowledgment now revalidates the current actor session inside the
owning identity transaction after profile/event writes and any replay receipt.
Stored same-key acknowledgments also revalidate before disclosure. A final denial
returns `session_unavailable`, so the owning transaction restores profile, events
and receipt together. Logout and deactivation retain their separate revocation
rules. Two API-host cases expire the clock after a real profile event appears,
check unchanged account/event state and absent failed receipt, then retry once
and check keyed replay without duplicate publication. Native execution remains
pending CI; compilation alone does not establish these acceptance outcomes.

The mandatory `test-identity-command-transactions.sh` release fixture adds a
CI-only invoker trigger that waits after inserting a real profile retry receipt.
It shortens the original session lifetime, requires observation of that exact
runtime-role INSERT in PostgreSQL `PgSleep`, and then requires HTTP 401 without
profile disclosure or a cookie. Complete account/session/audit/stream/event/receipt
state must match the pre-request snapshot. The trigger is removed, the original
expiry restored, and the existing concurrent same-key success/replay checks run
afterward. Bash syntax passes; exact-image execution remains pending CI. Runtime
grants and production schema remain unchanged.

The same native fixture additionally locks the profile replay table after a
successful save and observes the original same-key request waiting on its actual
receipt SELECT. It holds that read beyond session expiry, requires denial without
the saved profile or cookie, and compares complete state before/after. Restoring
the original fixture session must return the committed acknowledgment without
another state/event/receipt change. This covers final admission of existing
receipts separately from rollback of fresh saves. Syntax passes; execution is
pending the exact-image CI run.

The profile final-admission API-host cases now derive the actual original session
proof from its issued cookie, advance the injected clock to that exact expiry
after real profile event publication, and require the session proof to be restored
alongside account/events/receipt state. Strict compilation passes with no warnings
or errors; execution remains pending. The desktop/phone search timezone fixtures
also require keyboard Search focus and no automated WCAG 2.2 AA-tagged violations
after Board-policy changes/clearing. Browser TypeScript passes; automated checks
do not replace the remaining accessibility and native release acceptance work.

Search now retains the canonical `dueHasTime` flag. Date-only deadlines display
only the calendar date in the admitted Board/account timezone, using the shared
Card formatter; timed deadlines retain their explicit local time. UTC instants
must pass the shared precision/calendar validator before result admission, so
offset-free and invalid calendar values cannot reach rendering. A component case
uses the final microsecond of a Honolulu due day and requires a date without an
invented 23:59 time. Desktop/phone native fixtures set a real date-only deadline
and require that same calendar-only presentation. Native execution remains
pending; this does not establish full timezone or PRD acceptance.

The API search date-policy case also writes real timed and date-only deadlines
through the HTTP date endpoint. Every admitted search checks the canonical
`dueHasTime` flag and exact stored UTC instant while Board policy changes or is
cleared. The Honolulu date-only case requires the final PostgreSQL microsecond
of that local day; setting Board display policy must preserve it. Initial no-date
projection and outsider policy privacy checks remain covered. Strict compilation
is verified separately; API-host execution remains pending CI.

The mandatory native global-search fixture now edits an existing private Card to
a timed deadline and then a Honolulu date-only deadline, requiring search to
retain the timed flag, date context and final-microsecond UTC precision. Its
outsider query requires no date/context/Board/List disclosure. These checks run
after the original 50+2 continuation checks and before archived-parent checks;
the original collection coverage remains required. Bash syntax passes; native
exact-image execution remains pending.

### Executed local comment, activity and star recovery

On 2026-10-07, all six complete desktop/phone comment, activity and personal-star
native scenarios passed against the local Production API, restricted schema-110
PostgreSQL and current Vite source. Disposable Workers process only each newly
created fixture Organization, with global discovery disabled, and retire after
each invocation. These are scoped local runtime results, not current retained
release-image evidence.

Comments pass actual lost-response original key/body recovery, automatic timezone
changes in both open dialogs, unchanged stored history, offline edit recovery,
confirmed redaction, former-body receipt refusal, exactly three comment events and
the automated WCAG-tagged scan (two cases, 59.1 seconds). The first complete run
exposed a real offline-read defect: cleared rows left no displayed Card version,
so the recovery guard never read again. Recovery now performs fresh protected
admission even when the view was cleared, retaining a prior continuation only at
the same current Card version. A changed version starts the first page. Denial
still retires recovery, and successful reads clear the stale read-error notice.

Activity passes Board/Card paging, automatic account timezone recovery with
unchanged source datetime, body-free two-client updates/reconnect, access loss,
archived/deleted history, historical labels after rename/deactivation, keyboard
and automated WCAG-tagged checks (two cases, 2.6 minutes).

Personal stars pass privacy between accounts, original retry after a later change,
real private WebSocket delivery, immutable UTC history, automatic preference
recovery in an open mirror, unchanged Board/List data, closing focus and reload
recovery (two cases, 58.1 seconds). The first run exposed a real MUI closing-focus
defect: removing the activated Close button detached the ancestor used to identify
its own Dialog fallback. Retaining that exact Dialog reference restores the opener
while preserving the existing external-focus guard. The installed-Dialog regression
fails before the fix and passes afterward.

All 63 combined comment/activity/star/identity component cases pass, including
transient recovery with retained continuation and installed-Dialog close focus;
web typechecking/lint pass. Scenario timings are not performance benchmarks.
Current full release CI and the remaining preference consumers still require
acceptance; no issue closure follows from these six local scenarios.

### Executed local notification preference recovery

The complete [notification inbox scenario](notification-inbox.md#current-local-preference-and-reconnect-evidence)
now passes against local Production/restricted PostgreSQL/Vite with a disposable
fixture-scoped Worker. An independent recipient session changes locale/timezone
to en-US/Asia/Tokyo and then UTC; both desktop and phone automatically display
the new preferences while exact UTC datetime attributes and canonical stored
notification/read history stay unchanged. The original live delivery, lost read
acknowledgment with identical retry, bulk read, offline retained-cursor replay,
keyboard focus, access filtering and accessibility assertions all pass.

The first native run exposed surface remounting after transient admission failure;
hidden retained recovery now resumes the original live cursor after fresh access
admission. Actual denial destroys the feature tree. Inbox access denial separately
retires queued and background refresh until explicit fresh admission. All 166
focused notification/identity/surface/comment cases, web/browser typechecking and lint
pass. Current retained images, full CI and remaining date consumers still require
acceptance; this local scenario does not close AUTH-FR-010 or PRD-17.

The final shell also contains MUI portals within its mounted visibility boundary;
hidden dialogs relinquish focus enforcement and explicit access retry preserves
the unsent draft. The real installed-Dialog regression fails before the repair.
Final native notification execution passes in 36.4 seconds. Both complete native
comment cases pass again in 58.8 seconds after repairing a desktop retry race:
when a background Card read begins during a requested original receipt's account
preflight, the original key/body proceeds to the command endpoint's current
authorization checks. New changes still require local admission. A server failure
preserves the original intent; current server refusal still retires it. Both
focused gap regressions fail before the fix and pass afterward. The original
native lost-reply, private history, offline edit, redaction, former-body refusal,
exact source event, preference and accessibility assertions remain unchanged.

The complete web suite passed 1,881 tests across 134 files using two workers before
the final portal and comment-gap refinements. Its unrestricted-worker invocation
had 1,879 passes and two five-second filter-test timeouts; both affected files then
passed all 49 cases with two workers and unchanged timeouts/assertions. Final
focused checks and native runs above cover the refinements; the complete current
revision and exact-image gates still require CI. These observations do not claim
a final current-release green gate or performance benchmark.

The final isolated production web build also passes, with output outside the
checkout. This local build is not a retained release artifact; CI must build and
test its exact three images and pass the required gate for the committed revision.

### Open comment preference recovery

Clean Card comment views now recover account preferences through identity delivery
and visible periodic, focus, online and visibility checks. Recovery performs the
normal bounded account/page/account admission, retains the selected continuation
and renders the same stored UTC instant in the final admitted account timezone.
Signals wait while a draft, mention selection, original uncertain command or Card
admission is unresolved. They cannot replace the original retry key or body.
Access denial retires background reads until explicit renewed admission. Owned
comment action focus survives row replacement; focus moved elsewhere stays there.

The focused comment and identity suites pass all 30 cases, including continuation,
periodic fallback, cleanup, denied access and unchanged uncertain-command recovery.
Web typechecking/lint and browser typechecking pass. The mandatory desktop/phone
comment fixture now changes the account timezone from an independent session and
requires both clean dialogs to recover automatically without changing stored
comment history or issuing another comment write. Its strengthened native run
remains pending exact-image CI; this does not establish full AUTH-FR-010 acceptance.

The complete web suite against unchanged browser source at `28f9355` completed
with exit 0: all 122 test files and 1,494 tests passed in 491.71 seconds. This
includes canonical timed/date-only flag admission, invalid UTC calendar rejection,
Honolulu calendar-only display, and Board/account timezone precedence. Subsequent
backend and native-fixture increments did not change `apps/web` during that run.
Native API-host and exact-release-image verification remains pending; this source
result does not establish full PRD-02 or PRD-16 acceptance.

### Executed local date preference recovery

Card date captions and Board canvas due badges now recover admitted account
preferences from identity delivery and visible periodic, focus, online and
visibility checks. Board policy retains precedence; stored UTC dates and Reminder
scheduling remain unchanged. Denied access retires background reads, and an
account change requires fresh Card/Board admission.

Both complete desktop/phone Card-date cases and both Board-policy cases passed
locally on 2026-10-07. A separate signed-in session changed the timezone, open
views recovered automatically and canonical Board responses remained unchanged.
All 101 focused date/Board source cases passed. The native policy run also exposed
and verified repair of an initial StrictMode admission retirement defect. See
[date recovery evidence](card-dates.md#open-date-preference-recovery) for scope.
Current retained release images and complete PRD acceptance remain outstanding.

### Current account keyboard and accessibility evidence

The final 2026-10-07 combined native invocation passes all **22** scenarios from
`account.spec.ts`, `account-mention-handle.spec.ts` and
`identity-expected-account.spec.ts` in one 10-minute run. It uses the repaired
compiled API source at `58918eb7`, Production with email recovery enabled and
optional verification, restricted schema-110 PostgreSQL, a frozen production
web bundle and current Nginx/CSP. Normal API/edge limits and 25-second release
pacing remain enabled. This closes the prior split-invocation account evidence
gap for this local runtime; it is not current retained-image release acceptance.

The first complete invocation passes 21 cases and fails invitation original-ID
retry: its trace contains no second command, while background recipient recovery
runs during the keyboard action. The fixture now observes the real initial feed
reset and `INVITATION_ACCEPTED` frame, then waits for that recovery read before
explicit refresh/retry. The focused scenario passes, followed by all 22 cases
in the final combined invocation. The exact two original-ID commands, lost
acknowledgment, empty discovery, private-label withdrawal and canonical membership
assertions remain. No server admission, UI command guard or timeout is weakened.

The final run covers handle retries/conflicts/revocation, desktop/phone deactivation
and sign-in retries, sole-owner refusal, registration, logout/profile receipt
recovery, generic recovery/invalid reset links, persisted two-browser conflicts,
tagged WCAG checks and all eight stale-cookie command cases. The sixteen final
account-switch fixture users remain at version 1. Browser types, Nginx validation
and diff checks pass. Disposable API/web containers are removed; the original
three services remain. Actual Worker delivery, all other preference consumers,
performance and current complete release/PRD acceptance remain separate evidence.

On 2026-10-07, the full current web suite passed all 1,896 cases across 134 files
locally and in exact-commit `8c6d2b9a` CI. The strengthened two-browser profile
scenario passed real persisted preferences, canonical event/timestamp assertions,
conflict preservation/merge and logout recovery, plus tagged WCAG 2.2 AA axe checks
on desktop, phone and the phone conflict state. Both complete mention-handle
native cases also passed their original intent, concurrent account and revocation
checks. Browser typechecking passed after fixture changes.

The broader account invocation initially passed 11 of 14 cases. Targeted reruns
passed the original-ID invitation acceptance scenario after correcting a split
focus/Enter fixture race, and both unknown-account password recovery cases using
a disposable email-enabled Production API. All 14 distinct cases have scoped local
passes across those invocations; this does not establish a single complete green
release run or actual email transport. The disposable API/web fixture and its
private ephemeral key configuration were removed. See [account acceptance evidence](prd-02-acceptance.md#executed-account-browser-and-current-source-checks)
for exact scope and remaining acceptance.

### Account date source-calendar validation

The shared account formatter now rejects impossible calendar fields before
converting an explicit offset into the admitted locale/timezone. Previously
JavaScript normalized February 30, a non-leap February 29, April 31 and hour 24
into plausible dates on a later day. Such input now returns the existing
unavailable-date result instead of inventing a displayed date. The source
calendar is checked independently of its offset, so a valid leap date at
`+14:00` or `-14:00` still converts across UTC midnight correctly. Seven-digit
fraction input and existing daylight-saving/account preference behavior remain.

The original implementation fails all four new invalid-calendar regressions
(4 failed, 4 passed). After repair, all 43 selected formatter, Card date and
profile component cases pass. This is formatter/component evidence; it does not
prove every date consumer's live account-switch recovery, current immutable
browser release or complete PRD-02 acceptance. PRD-02 remains open at **16%**
estimated work remaining (planning estimate).
