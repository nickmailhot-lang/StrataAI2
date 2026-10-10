# Authentication and recovery support references

Sign-in, registration, password-reset request/consumption and email-verification
request/consumption now pair their fixed error wording with the validated
`X-Correlation-ID` from that response. They share the
[public reference contract](public-error-references.md): 1–64 ASCII letters,
digits, periods, underscores or hyphens. Missing or malformed metadata produces
no displayed reference. Provider/body diagnostics remain hidden.

Message and reference share one state value. Starting another attempt clears
the previous pair; local password-confirmation errors receive no server ID.
The response reference is captured before reading its JSON, so an unreadable
acknowledgment can still identify the actual received response. Network errors
without a received response get no fabricated reference. A response arriving
after cancellation cannot update the closed screen. Existing 15-second
deadlines, retry-key/body binding, confirmation rules, token withdrawal and
non-enumerating accepted notices remain unchanged.

## Executed verification

The two login/registration and four recovery reference regressions fail before
implementation: the selected baseline records 27 passes and six failures among
33 cases. After implementation, the initial complete authentication directory
passes 244/244 cases. Additional malformed-header/network, unreadable-body and
retry/late-response checks produce a final combined authentication and API
boundary result of **309/309 cases across 21 files**, zero failed or pending.
Original criteria and no-case-retry behavior remain. Typecheck, lint and a
current private Vite build pass. Reports/build output remain outside source in
`identity-error-references-20261010`. Initial setup commands used the wrong
working-directory/dependency path and did not run tests; they are not included
in either executed test count.

These are source and build results. Current native browser execution and
immutable-image CI are separate acceptance evidence and are not inferred from
the source tests. Profile, invitation and other screen consumers still require
review; this does not establish all error-reference or authentication acceptance.
PRD-02 remains open at **16% estimated work remaining**; PRD-01 remains open at
**34%** (planning estimates).
