# Same-origin browser request security (PRD-24 / ARCH-06)

All existing API mutations require `X-StrataAI-Request: 1`. GET/HEAD/OPTIONS
remain safe reads. The SPA's shared `apiFetch` transport sets this header for
unsafe methods and refuses external/protocol-relative target URLs. Non-browser
clients must explicitly send the same header.

The middleware rejects missing/invalid headers and cross-site or same-site
cross-origin fetch metadata with HTTP 403 and `csrf_rejected`, before auth or
mutation. Unknown routes retain their normal not-found/method behavior. No CORS
origins are allowed. Browsers cannot send the custom header cross-origin without
a successful preflight. The header is an intent signal, not a credential;
authentication and authorization remain necessary. Session cookies retain their
HttpOnly, SameSite and production Secure protections.

This follows the [OWASP API custom-header pattern](https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html#employing-custom-request-headers-for-ajaxapi),
with fetch metadata as additional protection. Adding cross-origin CORS later
requires reviewing this security boundary; wildcard/subdomain allowances are
not compatible with it.

`test-csrf.sh` checks blocked logout/deactivation/recovery requests, form login
attacks, hostile fetch metadata, denied CORS preflight and unchanged authenticated
state. It runs against both Demo and PostgreSQL release-image APIs. Normal account
and mutation flows plus Chromium E2E prove legitimate requests still succeed.
Rejection logs contain a correlation ID and no request body, email or token.

This implements SEC-FR-005 for current API routes. It does not complete PRD-24:
rate limits, file security, public response review and broader threat/authorization
coverage remain separate requirements.
