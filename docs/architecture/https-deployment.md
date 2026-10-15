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
- Owned keys, certificates, credentials, database, containers, volumes and
  network are removed. The original bundle remains unchanged.

The isolated account fixture explicitly enables self-registration and disables
verification/mail. It is a small functional deployment check, not load testing
or managed-provider acceptance. CI coverage guards reject gate omission,
conditional skipping, ignored/replaced failure, duplication and wrong ordering.
The overlay/listener are required release assets; `.pem`, `.key`, `.p12` and
`.pfx` files are refused even with recalculated bundle checksums.

## Executed supporting evidence (2026-10-15)

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

## Remaining deployment acceptance

The actual hostname, trusted public certificate, certificate renewal/expiry
monitoring, DNS/firewall routing, external mail/storage/scanner settings,
durable keys, backup operation and complete current-image CI still require
their own evidence. A localhost certificate test is not a public deployment
or completed ARCH-12/MVP acceptance. Large-load testing remains post-MVP.

## References

- [Nginx HTTPS server and certificate-chain configuration](https://nginx.org/en/docs/http/configuring_https_servers.html)
- [Compose replacement and reset semantics](https://docs.docker.com/reference/compose-file/merge/#replace-value)
