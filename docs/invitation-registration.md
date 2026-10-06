# Invitation-backed account registration

`POST /auth/register` accepts an optional `invitationToken` in its JSON body. Without it, the existing self-registration policy applies. With it, a current valid invitation must match the normalized email even when public registration is enabled. Empty, oversized, expired, revoked, consumed, wrong-email or no-longer-authorized proof is rejected generically before account or receipt disclosure. Production verified-email signup fails closed when verification delivery is disabled.

The existing identity command owns one PostgreSQL transaction and connection. Invitation routing is only a hint: the adapter sets the actual Organization context temporarily, freezes the active parent, issuer membership and canonical invitation, then freezes issuer/existing recipient accounts in PostgreSQL UUID order. It checks canonical email, surface/role, issuer lifecycle/grant and configured verification policy. Tenant context is cleared before global identity operations and in every adapter exit. It never opens a second tenant connection inside the global identity command or fabricates an Organization for account records.

Registration creates only an account, its verification token/delivery where required, account audit/event and retry receipt. It does not consume the invitation, create membership or grant Portal access. With verification required the account stays PENDING_VERIFICATION; verification and explicit invitation acceptance remain separate. Existing accounts are reused through sign-in, or credential-checked acknowledgment of the same signup; account uniqueness prevents duplicate identities. Portal acceptance remains separate from Internal membership. Board-specific invitations bind the active Board and role during registration proof; their explicit acceptance and grant behavior are documented in [Board invitations](board-invitations.md).

Signup receipt HMACs bind the invitation token hash alongside existing registration inputs, using a separate purpose. Existing self-registration fingerprint bytes remain unchanged across upgrades. No bearer or password is stored in the receipt. Replays require fresh invitation authority and matching credentials before returning an acknowledgment. Production rechecks the locked invitation after account/audit/event/receipt writes, rolling back the entire transaction if expiry occurs during a write wait. Demo shares the account/Organization command gate and captures Identity rollback participants. It now rechecks invitation authority after account, verification-token, event and receipt writes; final refusal restores those writes before releasing the gate. Registration reads invitation state but does not mutate it, so its rollback does not capture or restore Organization/Invitation stores. These process-local snapshots do not establish durable rollback or real database-wait equivalence.

The invitation link's embedded MUI registration form supplies the in-memory proof and keeps the same body/key for an uncertain retry. Signup acknowledgment does not accept the invitation. Required verification tells the recipient to verify and reopen the original invitation. Sign-in requests exclude invitation proof. The bearer is neither displayed nor stored in browser storage.

Validation: warnings-as-errors solution build, web typecheck/lint, all 212 web cases and actual desktop/mobile signup and existing link browser scenarios pass locally. Two API-host cases cover both surfaces under closed registration with verification, wrong-email rejection, stable retry, changed-proof rejection, no premature membership, verification and subsequent explicit acceptance. Windows Application Control prevents local API test execution; Linux CI executes these cases.

The mandatory exact-image signup fixture uses closed registration and a one-connection pool to check both surfaces, concurrent identical acknowledgments, atomic audit/event/receipt rollback, current issuer after a parent wait, expiry during an audit write wait and absence of early grants. The existing separate Worker mail fixture additionally covers closed registration under the default verification policy, real verification delivery and explicit Internal/Portal acceptance. Two required keyboard browser scenarios use isolated ephemeral fixtures outside release bundles. These new release checks remain pending until CI completes; local source/browser checks do not substitute for them.

Remaining PRD-60/PRD-03 work includes transactional invitation email publication and separate Worker delivery, administrator history/revocation UI and replay behavior, Board-specific access, and the remaining event/observability/accessibility/performance and acceptance requirements. This increment does not close either ticket.

## Demo registration expiry rollback

`InvitationRegistrationRollbackTests` covers Internal, Portal and Board-specific
proof under closed registration with verification required. Its test decorator
forwards to the actual Demo proof provider: it first observes the real pending
account, verification token, `USER_REGISTERED` event and registration receipt,
then advances the injected clock to the invitation expiry before the final check.
The request must return only `invalid_or_expired_invitation`, with no account ID
or bearer disclosure. After restoring the test clock, checks require the account,
verification token, event and retry receipt to be absent; this prevents natural
token expiry from hiding an incomplete rollback. The invitation remains unchanged
and Organization, Portal and Board grants remain absent.

A fresh request with the same registration key must create one pending account;
a matching replay must return the same acknowledgment without another event or
premature access grant. The cases require native API-host CI execution;
warnings-as-errors compilation alone does not prove their runtime acceptance.
Production final-check and transaction behavior remain unchanged. Demo audit
storage is a no-op, so these cases do not prove durable audit or mail rollback.
