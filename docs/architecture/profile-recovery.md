# Profile recovery

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
