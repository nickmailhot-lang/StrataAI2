# Invitation proof in a request body

`POST /invitations/accept` accepts `{ "token": "..." }` for an authenticated, verified recipient. New recipient flows must use this endpoint rather than placing bearer tokens in URL paths. The compatibility route remains available to existing callers.

The endpoint delegates to the existing invitation command: exact verified email, current issuer grant, Organization lifecycle, expiry, revocation and session checks still apply inside the Organization transaction. Tokens are one-use. A lost response must be recovered through verified-email discovery and the retry-safe `/me/invitations/{id}/accept` acknowledgment; submitting a consumed bearer again does not restore access or acknowledge a fresh grant. Responses never include the token. Empty or oversized proof is rejected with the same generic invalid-invitation response.

API-host cases cover both surfaces, wrong accounts, anonymous requests, malformed proof, consumed tokens and removed membership. The mandatory exact-image invitation fixture supplies an isolated, administratively hashed proof without revealing a production token through HTTP, then checks both surfaces, recipient binding, one-use behavior and a single acceptance audit.

Validation: warnings-as-errors solution build and fixture shell syntax pass locally. Windows Application Control prevents local API test execution; required Linux CI executes those cases. Release-image verification remains pending for this change. This is an endpoint prerequisite: email publication/Worker delivery, recipient link UI, closed-registration signup, administrative invitation history and the remaining onboarding acceptance criteria are unfinished. No ticket is complete based on this endpoint alone.
