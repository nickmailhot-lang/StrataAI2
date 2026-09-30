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

This increment does not complete PRD-02. Production email delivery,
realtime update/reconnect recovery and broader accessibility evidence remain
to be implemented and verified before closure.

`tests/browser/account.spec.ts` runs Chromium against the actual web/API release
images and PostgreSQL. It checks registration, sign-in through Nginx, profile
persistence, two-page stale-save recovery and logout/session revocation. Browser
traces and screenshots are retained on failure. Nginx and the Vite development
proxy forward the API's top-level routes as well as `/api`; `/app`, `/login` and
`/portal` remain SPA routes.
