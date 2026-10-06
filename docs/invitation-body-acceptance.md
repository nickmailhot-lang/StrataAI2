# Invitation proof in a request body

`POST /invitations/accept` accepts `{ "token": "..." }` for an authenticated, verified recipient. New recipient flows must use this endpoint rather than placing bearer tokens in URL paths. The compatibility route remains available to existing callers.

The endpoint delegates to the existing invitation command: exact verified email, current issuer grant, Organization lifecycle, expiry, revocation and session checks still apply inside the Organization transaction. Tokens are one-use. A lost response must be recovered through verified-email discovery and the retry-safe `/me/invitations/{id}/accept` acknowledgment; submitting a consumed bearer again does not restore access or acknowledge a fresh grant. Responses never include the token. Empty or oversized proof is rejected with the same generic invalid-invitation response.

API-host cases cover both surfaces, wrong accounts, anonymous requests, malformed proof, consumed tokens and removed membership. The mandatory exact-image invitation fixture supplies an isolated, administratively hashed proof without revealing a production token through HTTP, then checks both surfaces, recipient binding, one-use behavior and a single acceptance audit.

Current validation: the Release solution build passed with zero warnings and
errors, and 42 selected invitation API regressions passed locally, including
body-proof acceptance and Board/Portal separation. The fixture passed Bash
syntax validation. Body-proof activation of internal Organization membership
now also writes the actual membership's `ORGANIZATION_MEMBER_ADDED` audit;
Portal and Board invitations retain their separate behavior. See
[the acceptance audit contract](architecture/invitation-discovery.md) for its
transaction and remaining event-delivery boundary. Current exact-image
persistence checks remain pending. No ticket is complete based on this endpoint
or these source checks alone; consult current acceptance records for the
remaining onboarding requirements.
