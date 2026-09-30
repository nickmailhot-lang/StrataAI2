# Account profile management (PRD-02)

The MUI profile screen loads the authenticated account from `GET /me` and saves
display name, avatar URL, locale and timezone through `PATCH /me`. Email and
account status remain read-only. The screen displays the returned server state,
preserves edits on failed saves, supports retry/discard, and redirects to sign-in
when the session expires. Failed sign-out leaves the user on their profile with
a retry message.

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

This increment does not complete PRD-02. Production email delivery, profile
concurrency control, realtime update/reconnect recovery, broader accessibility
and browser E2E evidence remain to be implemented and verified before closure.
