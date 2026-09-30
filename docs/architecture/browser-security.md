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

Authentication endpoints share an ASP.NET fixed-window budget of 60 requests per
minute per socket peer; invitation routes have a separate 60/minute budget per
authenticated user (or socket peer when anonymous). No requests are queued.
`STRATAAI_AUTH_REQUESTS_PER_MINUTE` and
`STRATAAI_INVITATION_REQUESTS_PER_MINUTE` accept 10–1000; invalid settings fail
startup. HTTP 429 returns `rate_limit_exceeded` and a numeric `Retry-After`.
Logs record `RATE_LIMIT_TRIGGERED` with a correlation ID, without credentials.

Nginx independently limits sensitive paths by its actual socket peer to 60/minute
with a burst allowance of 20. Arbitrary X-Forwarded-For/X-Real-IP headers never
change the limiter key. API peers behind Nginx share the API authentication budget;
operators must size it for expected aggregate traffic. Additional trusted proxies
or multi-replica deployment require an explicit proxy/distributed-limit design.
These are process/edge-local limits, without Redis or a new broker.

`test-rate-limits.sh` exercises both direct API and edge endpoints after legitimate
browser tests, checking invalid requests, spoofed forwarding headers, HTTP 429,
retry metadata and unaffected health routes. This increment implements SEC-FR-005
and authentication/invitation coverage for SEC-FR-010. PRD-24 remains incomplete:
future upload/mention/public-share limits, file security, public response review
and broader threat/authorization coverage remain required.

# Release response headers

The web image supplies CSP on HTML, proxied API responses and edge errors, plus
`nosniff`, `DENY` frame protection, `no-referrer`, and disabled camera/microphone/
geolocation permissions. Scripts and network connections are same-origin;
inline scripts, remote scripts, embedded objects, base overrides and framing are
blocked. HTTPS avatar images and data images remain supported.

MUI/Emotion currently needs inline styles, so only `style-src` permits
`unsafe-inline`. Script policy never permits it or `unsafe-eval`. Moving styles
to request-specific nonces requires a nonce-aware HTML/Emotion delivery design;
do not insert a static reusable nonce. See [MUI CSP guidance](https://mui.com/material-ui/guides/content-security-policy/).

The shared include is repeated in the rate-limit error location because its
Retry-After directive replaces inherited Nginx header directives; see
[Nginx header inheritance](https://nginx.org/en/docs/http/ngx_http_headers_module.html).
Exact-image browser tests verify 200/401/403/404 headers and observe both inline
and external script injection being blocked. Rate-limit tests verify 429 headers.
The existing account/recovery browser flows run under this CSP. Vite development
mode does not represent the release header boundary.

TLS termination must enforce HTTPS and HSTS at the deployment's trusted edge;
the internal HTTP Nginx container does not infer TLS from arbitrary forwarded
headers. Secure production session cookies remain enforced by the API.
