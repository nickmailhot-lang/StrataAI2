# Board invitation release evidence

## Verified directory, consent and visibility baseline

Commit `94dbcbb8b5b62063e21c7ffb7c4add622be55ec4` passed all nine jobs in
[CI run 36920524051](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36920524051).
Decoded logs confirm 123 domain, 169 API host and 256 web tests, with no skipped
.NET tests. PostgreSQL checks include migrations, forced RLS/pgvector, canonical
routing, audit immutability and restricted runtime capabilities. The required
gate records successful source, build, container and security inputs.

Exact-image fixtures passed current history/revocation/member-directory denial
after Board lock waits; both-role Board signup/acceptance and actual signed mail
transport; bounded 53-person directory paging with real foreign keys and safe
current/former profiles; stale/malformed membership consent with unchanged state;
and keyed role/removal acknowledgments that do not reapply to later membership.
The main browser batch executed 37 scenarios, including desktop/mobile Board
issuance, lost revocation, bound proof acceptance and visibility consent/conflict/
lost-response recovery. Its one skipped identity-mail scenario passed separately
in the dedicated Worker-delivery browser step.

Three non-expired exact-SHA artifacts were inspected:

| Artifact | ID | SHA-256 digest |
| --- | --- | --- |
| Tested image archives | 11191623680 | `7eab4ffa569f782cdc907b4e483e70789a3e4c64aad1bf1e7b5276146db6faa4` |
| Security evidence | 11192645165 | `c4d90785f261894777f667637937e196933318680ada3683485858c684243e50` |
| Docker release bundle | 11193413515 | `33fc7ee74d53f48fb2112b7fddb47599a696a4160a736a223a57c07f129cb232` |

The release job copied the three retained image archives; it did not rebuild.
These are artifact archive digests. This verified baseline predates the member
management UI/browser scenarios, live member/visibility administration,
collaboration/outage fixture, focus/warning refinements and batched profile query.
Those later increments need their own release evidence. PRD-05 and PRD-60 remain
open; telemetry, performance and complete acceptance are not established here.

## Verified public issuance, mail and revocation baseline

Commit `ec224acef39c5d494d6cb2be90391298c5aec52e` has a fully verified
[CI run 36913682034](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36913682034).
All nine jobs completed successfully. Decoded logs confirm 123 domain, 166 API
host and 246 web tests. Required-gate source/build/container/security inputs all
report success; the release job copies retained tested image archives.

Exact API/Worker image logs explicitly confirm both Board invitation roles through
public signed issuance, atomic sibling publication rollback, actual scoped Worker
and provider delivery, separate verification, proof review/discovery and explicit
Board grants. The exact commit's transport script runs Internal, Portal, Board
Admin and Board Member cases and reads actual SENT history. Consumer and revocation
logs also verify rollback, one audited natural-ID acknowledgment and signup denial
after revocation. Both Board proof-review browser scenarios passed at desktop and
phone widths. The main browser batch passed 33 scenarios; the single skipped
identity-mail scenario ran and passed separately in its dedicated step.

Three non-expired artifacts belong to this exact commit:

| Artifact | ID | SHA-256 digest |
| --- | --- | --- |
| Tested image archives | 11189140799 | `34f5d7e21bc2c160f0fb19ef7919425d72abb13a7e8c984ddcad915921a96584` |
| Security evidence | 11189930916 | `2f4eaa2c1ec9e0c0bf775c58f7ff3a25e02c7a1257200ee0e51df8f0e672b1c0` |
| Docker release bundle | 11190408896 | `082f7aeecfca1dfbdb27afec678ae803a79b7a65219ef51a9f2c7506b5efa527` |

These artifact digests identify GitHub archives, not Docker image digests. This
verified baseline predates the Board history UI, sender/history browser additions,
new lifecycle wait checks, visibility and member-management screens, member
directory pagination/profile enrichment and membership version preconditions.
It does not establish release success for those subsequent commits or complete
PRD-05/60 acceptance. Later frontend/backend CI must be assessed at its own SHA.

## Verified baseline

Commit `589aaf432fdc050dc1b300ea45267b64ed570e13` was verified in
[CI run 36909462342](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36909462342).
All nine jobs completed successfully, including the required gate and release
bundle. Decoded source logs report 123 domain tests, 164 API host tests and 240
web tests, with no skipped .NET tests.

The exact-image container logs verify both Board roles through closed signup and
explicit acceptance, rollback, no early grants, role preservation, one-use proof,
non-restoring retry, fresh signup authority after a Board lock wait, and Board
administrator continuity. Both Board proof-review browser cases (1280px and
390px) executed successfully. The main browser batch passed 33 cases and skipped
one identity-mail scenario that executed separately and passed in the dedicated
mail browser step. Collection alone was not used as execution evidence.

Three non-expired artifacts are attached to that exact SHA:

| Artifact | ID | SHA-256 digest |
| --- | --- | --- |
| Tested image archives | 11185294523 | `53e8fa1e9c290a12492df703603eec563c539db7f705e4ebaf9f5f930c14db95` |
| Security evidence | 11186406261 | `63385b36650bbd6babaaf909c669bcb75baaabe28721726d08b5bff33fd0a456` |
| Docker release bundle | 11188568124 | `9f332d7cbd94d64aedb53bce7c896c897c8bc7ae8d3edbceee24aef2c946acf7` |

The release job copies the retained web/API/Worker image archives into the bundle
after the gate; it does not rebuild them. These digests identify GitHub artifact
archives, not individual Docker image digests.

## Evidence boundaries and remaining work

This baseline predates the public Board issuance endpoint, actual Board mail
transport scenarios, sender UI, Board history/revocation API and UI, sender/history
browser scenarios, and history/revocation lifecycle checks after database lock
waits. Its actual invitation-mail assertions cover ordinary Internal and Portal
delivery, not the later Board Admin/Member transport scenarios. It must not be
used to claim those later increments passed release CI.

PRD-05 still requires release evidence for the new Board member and visibility
screens, together with current-authority,
concurrency, retry, keyboard, mobile and realtime evidence for those controls.
BoardScreen links to invitation creation/history, visibility and member management.
Server APIs alone do not satisfy client acceptance. See `board-visibility-ui.md`
and `board-members-ui.md` for the current UI increments and their evidence limits.
Telemetry, performance and all other ticket-specific definition-of-done items
also require scoped evidence. PRD-05 and PRD-60 remain open.
