# Board invitation release evidence

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

PRD-05 still requires complete user controls for visibility and Board member role
changes/removal, together with current-authority, concurrency, retry, keyboard,
mobile and realtime evidence for those controls. BoardScreen currently links to
Board invitation creation/history; it does not expose those other administration
controls. Their server APIs do not by themselves satisfy client acceptance.
Telemetry, performance and all other ticket-specific definition-of-done items
also require scoped evidence. PRD-05 and PRD-60 remain open.
