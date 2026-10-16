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

## Browser origin interpretation and redirect fencing — 2026-10-15

The shared [API transport](../../apps/web/src/api/apiFetch.ts) now validates the
browser's parsed URL origin as well as the leading slash. The previous check
accepts slash/backslash and slash/control-character/slash strings that resolve
to an external authority. Four regressions require refusal with the existing
fixed message before fetch is called. Invalid URL parsing also fails closed.

Every request additionally enforces `mode: 'same-origin'` after caller options,
so callers cannot select `cors`/`no-cors` to weaken the boundary. This also fences
cross-origin redirects and resolution against a changed document base. Normal
paths/query strings, credentials, bodies, cancellation, expected-account headers,
the mutation CSRF header and Problem normalization retain their contracts.
The [browser request-mode contract](https://developer.mozilla.org/en-US/docs/Web/API/Request/mode)
documents rejection of another origin in this mode.

All **seven new regressions fail before repair**; the other five original
transport cases pass. After repair all **348 selected tests in 22 API, auth and
route files** pass, including all 12 transport cases. Web TypeScript, targeted
lint and the production Vite build pass. The existing bundle-size warning
remains; no size threshold is relaxed.

An independent actual Chromium fixture serves the compiled production sign-in
screen from one local HTTP origin and redirects its POST with 307 and 308 to a
second local origin. **Before repair, each case forwards one synthetic sign-in
POST to that second origin. After repair, both cases keep exactly one original
POST and zero second-origin POSTs**, with the existing explicit retry message.
The fixture deliberately omits release CSP and permits CORS to isolate the
transport's own protection. This does not reproduce an exploited deployed
configuration: the release CSP provides an additional same-origin boundary.
No real account, external destination or backend authorization is involved.

Both native executions retain all **362 captured frontend source/output files**
unchanged, and close their owned browser contexts and both local HTTP servers.
The locally built metadata identifies parent `0ad6b3d7`, version `0.1.0-1736`;
this is compiled-screen transport evidence rather than a current immutable-image
release or full application acceptance. No application backend, database or
container starts; no image is built, pulled or imported. No external email is
sent, and the original database/local Demo are
unused. These are tiny functional fixtures, not deferred large-load tests.

Mandatory web unit CI includes these regressions. Current exact-commit/full CI,
wider authorization/privacy review and the remaining requirements still govern
closure. **PRD-24 remains open at 35% estimated work remaining** (planning
estimate covering the full security ticket, not a failed-test percentage).
ARCH-02 and the operational MVP remain open.

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
