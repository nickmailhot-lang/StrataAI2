# Assembled release payload startup

ARCH-11-FR-082 requires the downloadable payload to run without application
source, Node.js or the .NET SDK. Checksums and a nonempty archive alone do not
prove that its Compose, migrations, role provisioning and runtime instructions
work together. ARCH-11-FR-052 also requires API/Worker shutdown and restart proof.

After the complete release input/bundle checks and before artifact upload, CI
runs `scripts/ci/test-release-bundle-startup.sh bundle`. The check enters the
assembled folder and refuses application source folders, `.git` or an existing
operator `.env`. It uses only that payload's archives, metadata, Compose file,
migrations and supplied runtime scripts. It does not compile or rebuild images,
mount application source or invoke Node/.NET SDK tools. Bash, Docker/Compose,
curl, jq and openssl are CI orchestration tools; the ordinary operator commands
retain their documented requirements.

The check creates a uniquely named Compose project and disposable secrets, masked
on GitHub Actions. It loads each archive and matches the retained image ID, starts
a fresh PostgreSQL/pgvector volume, applies all ordered migrations with the
payload's advisory-lock runner, and provisions separate restricted API/Worker
roles. The actual Production hosts must pass the supplied health script, including
readiness and the web-to-API proxy. Web/API/Worker runtime revision/version and
running image IDs must match the payload.

API and Worker are stopped with a 20-second bound. Both must exit 0 without OOM,
then [Compose start](https://docs.docker.com/reference/cli/docker/compose/start/)
must restart the same container IDs and pass all health/identity checks again.
Cleanup removes only the owned Compose project, including its fresh volumes and
network, and its generated `.env`. Surviving owned containers/volumes or a failed
cleanup make the check fail. The original bundle verifier runs again after the
smoke, before upload, requiring unchanged original metadata/provenance/checksums
and refusing any leftover private file. No failure can still reach release upload.

Fifteen workflow mutations protect the startup gate, the post-startup validation
gate and mandatory CLI refusal regressions against omission, replacement with
success, skipping, ignored failure and invalid ordering. Two actual Bash CLI tests
require existing operator settings to survive and application checkouts to be
refused before secret creation or Docker/SDK commands. The tests remove external
commands from PATH to prove early refusal. They exposed an initial combined shell
guard that continued past an existing `.env`; separate mandatory checks fix it.
Combined workflow/metadata/CLI tests pass **181/181**; release validation remains
**21/21**. The required integration matrix
remains four groups, seven executions and 112 registered checks.

## Local source-free payload evidence, 2026-10-09

The complete helper passes on Docker Desktop using the three exact exported and
reloaded archives from `1176d4929ac5c44471f48ec1ac3034edd47f6b87` and the committed
operator scripts/Compose/migrations. The private payload contains no `.git`,
`src` or `apps` folder. Its metadata is explicitly local; no GitHub workflow
identity, successful security scan or SBOM is fabricated. This is operator-payload
startup evidence, not a full CI release bundle validation.

Fresh database migration and restricted Production startup, all six health checks,
all three runtime/image identities and graceful API/Worker stop/start pass; the
process exits 0. Independent verification finds zero owned containers, volumes
and networks, and no generated `.env`. All three archive hashes are unchanged.
The three pre-existing running services remain intact.
The final credential-cleanup implementation also passes the complete startup and
restart invocation. Cleanup is installed before any secret content is written,
so normal script failure during a write invokes credential removal. Abrupt host
or process termination can bypass shell traps; crash-safe erasure is not proven.

The host already had Docker and cached dependencies. This does not establish a
physically clean host, cold-start timing, deployed object storage/mail/scanner
settings, optional metrics/attachment overlays, production job durability under
restart, or successful current full GitHub CI. The CI check still needs execution
on its gated candidate and retained artifacts. ARCH-11 remains open.
