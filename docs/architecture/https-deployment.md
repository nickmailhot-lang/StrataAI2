# Optional HTTPS deployment and verification

The [operator release guide](../release/README.md#optional-https-on-a-linux-docker-host)
provides an optional Linux Docker configuration using the same retained web,
ASP.NET API and separate Worker images. An approved external TLS ingress can
also terminate HTTPS. PostgreSQL/RLS/pgvector and attachment storage boundaries
remain unchanged; this option supplies no cloud resources or certificates.

The web image includes an empty optional listener file. The HTTPS overlay mounts
the [TLS listener](../../deploy/https/nginx-tls.inc) and an existing private
certificate directory read-only. It replaces the web's published ports with
443 and clears direct API/Worker host bindings, using Compose 2.24.4+ merge
tags. Nginx retains private HTTP for its container health check and serves the
same routes through TLS 1.2/1.3. HSTS is emitted only on HTTPS through the shared
security-header include, including locations that define their own headers.

## Required release gate

[The maintained HTTPS contract](../../scripts/ci/test-release-https-startup.sh)
runs against the assembled source-free payload after the normal Production and
Demo startup gates and before final unchanged-payload verification/upload.
It uses only already-loaded retained application images, a fresh PostgreSQL
database, all production migrations, restricted runtime roles and an ephemeral
localhost test certificate. It does not build, import or download images.

The gate requires:

- Missing certificate and mismatched private key refuse Nginx configuration.
- API/Worker have no published ports; only web TLS is published.
- Runtime certificate/configuration binds are read-only, and private HTTP does
  not emit the HTTPS-only HSTS header.
- Untrusted certificates and a trusted certificate with the wrong hostname
  fail client verification; ordinary HTTP on the TLS port returns 400.
- HTTPS static/build/API responses have the exact candidate identity and HSTS.
- Production sign-in sets a Secure cookie; the normal HTTPS cookie jar then
  authenticates without overriding cookie security.
- A real HTTP-created Organization/Board/List/Card graph and saved session
  remain available through HTTPS after web restart, with unchanged image IDs.
- A scoped real Worker publishes a Card edit to an authenticated `wss:` Board
  stream. Reconnecting from its persisted cursor recovers a disconnected edit
  with the same event ID as the authorized HTTP journal.
- Foreign-Origin negotiation returns the exact 403 problem code. An anonymous
  stream completes TLS/SignalR handshake but closes without any private page;
  malformed or partial output cannot count as a safe refusal.
- Logout starts from an open recovered connection, closes it within eight
  seconds and makes the old cookie fail `/me` with 401.
- Owned keys, certificates, credentials, database, containers, volumes and
  network are removed. The original bundle remains unchanged.

The isolated account fixture explicitly enables self-registration and disables
verification/mail. It is a small functional deployment check, not load testing
or managed-provider acceptance. CI coverage guards reject gate omission,
conditional skipping, ignored/replaced failure, duplication and wrong ordering.
The overlay/listener are required release assets; `.pem`, `.key`, `.p12` and
`.pfx` files are refused even with recalculated bundle checksums.

The CI harness uses Node 24's built-in fetch/WebSocket client and the protected
fixture cookie jar. The fixture CA is supplied through `NODE_EXTRA_CA_CERTS`;
certificate validation remains enabled. No package install or application image
is added. This client lives in the CI source directory outside the assembled
payload. Operator startup still needs no Node/.NET SDK on the Docker host.

## Initial HTTPS evidence (2026-10-15, e5570793)

The complete maintained driver passes locally in **92.479 seconds** with all
**766 captured inputs matching**. It uses the archive-verified retained images
at `24f562464f706884e4a27577b61c65ff385b4263`, mounting the current Nginx and
shared-header configuration into that older web image. The actual operator
overlay and unmodified driver are exercised; this does not certify a newly
built current-main web image. The default listener stub and image COPY require
their own complete current-image CI.

All certificate refusal, trust/hostname, private/read-only binding, HTTPS-only
HSTS, Secure cookie, one-Card hierarchy and same-container web-restart checks
pass. The full original Docker inventory is restored, 760,354 owned temporary
bytes are removed, and no existing local database or image build/import/download
is used. An initial run stops after Docker reassigns its ephemeral published
port on restart; the fixture now pins the allocated port before testing that
same listener and session. That fixture issue is not a deployment-port defect.

The 236 CI coverage guards and 37 release-artifact tests pass, including six
HTTPS gate mutations, two missing-asset subcases and four private TLS format
refusals with regenerated checksums. Workflow lint, 510 local documentation
links and four case-sensitive private-file ignore probes pass; no probe files
are created. Docker-context exclusions are configured, with actual current-image
builds still pending. Current exact-image
hosted execution, actual public certificate operation and full MVP acceptance
remain pending. This remains a small functional check; large loads stay deferred.

## Secure live Board evidence (2026-10-15)

The expanded complete maintained driver passes in **103.298 seconds**, with all
**767 captured inputs matching**, the full original Docker inventory restored
and **771,716** owned temporary bytes removed. As above, it uses archive-verified
`24f56246` images with the current Nginx/header files mounted, rather than newly
built current-main images. No application image build/import/download, original
database use or large-load test occurs. The original certificate, HTTPS cookie,
persisted graph and same-container restart cases still execute.

The real separately scoped Worker delivers the version-2 Card update through
TLS. Closing that connection, editing once while disconnected and resuming its
cursor yields version 3, excludes the already-consumed version 2 and matches the
persisted journal event ID. Canonical Card data contains the final title/version;
frames exclude the private titles and reject extra content/metadata fields.
The foreign-Origin negotiation, anonymous-stream refusal and actual logout
withdrawal checks pass. Anonymous denial requires successful handshake, closure,
zero pages and no malformed/partial data. Logout must begin with the connection
still open; a previously closed socket cannot satisfy withdrawal evidence.

All **29** client-evidence tests pass: secure-cookie/target refusal, exact scope/
actor/entity/version matching, private/foreign/duplicate/malformed page refusal
and unsafe anonymous-denial states. The **241** CI coverage guards pass,
including five new Node-runtime/ordering/source-test omission mutations.
Complete current-image CI, browser acceptance and managed deployment remain
separate requirements. These checks use one synthetic Card and a few sockets.

## Certificate replacement and restoration evidence (2026-10-15)

The maintained assembled-bundle gate now executes the documented certificate
replacement procedure on the running listener. A mismatched replacement key
must fail `nginx -t`; a fresh verified connection must still receive the original
certificate, and the original session and saved task graph must remain valid.
A new matching certificate/key pair then passes validation and reload. Fresh
TLS handshakes must present that exact new SHA-256 certificate fingerprint,
and the original certificate alone must no longer establish trust. Session and
complete saved-graph equality checks use only the replacement certificate as
their trust input.

Restoring the original pair repeats validation/reload and verifies its exact
served fingerprint. The replacement-only trust input must now fail, while the
original session and graph still pass with the original certificate. The web
container ID and start timestamp stay unchanged across both reloads, and all
three application image IDs remain unchanged. The existing WSS/Worker/replay/
withdrawal checks execute after restoration; this does not establish continuity
of a previously open socket through certificate replacement.

The complete expanded gate passes locally in **119.982 seconds**, with all
**767 captured inputs matching**, original Docker inventory restored and
**775,054** owned temporary bytes removed. It reuses archive-verified `24f56246`
images with the declared current Nginx/header mounts; no application rebuild,
image import/download, original database use or large load occurs. All **29**
HTTPS client tests and **241** CI coverage guards pass, as do Bash syntax and
workflow lint. This is supporting retained-image verification, not a completed
current-image release or public certificate-provider deployment.

## Remaining deployment acceptance

The actual hostname, trusted public certificate, certificate renewal/expiry
monitoring, DNS/firewall routing, external mail/storage/scanner settings,
durable keys, backup operation and complete current-image CI still require
their own evidence. A localhost certificate test is not a public deployment
or completed ARCH-12/MVP acceptance. Large-load testing remains post-MVP.

## References

- [Nginx HTTPS server and certificate-chain configuration](https://nginx.org/en/docs/http/configuring_https_servers.html)
- [Nginx configuration reload and existing worker behavior](https://nginx.org/en/docs/control.html)
- [Compose replacement and reset semantics](https://docs.docker.com/reference/compose-file/merge/#replace-value)
- [Node's Undici WebSocket client options](https://github.com/nodejs/undici/blob/main/docs/docs/api/WebSocket.md)
- [ASP.NET Core SignalR hub protocol](https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md)
