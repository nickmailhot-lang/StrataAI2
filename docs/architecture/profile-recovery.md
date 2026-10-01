# Profile recovery

The MUI profile form reads the authoritative profile at most every ten seconds while visible and also on focus, reconnection and visibility recovery. One read is active at a time. A fifteen-second deadline covers both transport and response-body parsing; timeout aborts the request, releases the read slot and fences an ignored-abort response. Unmount/reload removes listeners and timers and aborts the owned request.

Recovery reads require the profile fields and a positive safe-integer version. Older/equal versions cannot replace a newer profile or draft. Starting a save or sign-out changes the read epoch, so an earlier read cannot overwrite its acknowledgment. Session denial clears the view and redirects to sign-in; a changed subject also requires sign-in rather than retaining the previous draft.

A clean form adopts newer name/avatar/locale/timezone values automatically. A dirty form preserves its exact draft and original version, updates the authoritative summary and announces that the profile changed elsewhere. Saving stays disabled until explicit discard-and-load-latest. Read failure preserves the form and exposes an accessible retry notice; periodic/focus recovery continues. This bounded recovery supplements, rather than completes, the still-pending identity event/replay contract.

Account status uses canonical API strings (`PENDING_VERIFICATION`, `ACTIVE`, `SUSPENDED`, `DEACTIVATED`) rather than numeric enum ordinals. This matches the profile contract and provides an explicit lifecycle label.

Component checks cover periodic/focus recovery, dirty drafts, background denial, an abort-ignoring timeout and late results after timeout/save. The required account browser scenario uses desktop and phone-sized profile views, automatic conflict discovery and clean-form recovery, explicit draft discard, and automatic logout recovery without manual page reloads. These checks do not complete durable identity idempotency/events, timezone date rendering, ownership continuity or all PRD-02/60 criteria.
