namespace StrataAI.Infrastructure.Organizations;

// These stores require the account/Organization gate as well as the Work gate.
// Registering them as Work-only participants could restore over identity writes.
internal interface IDemoOrganizationTransactionParticipant
{
    Action CaptureRollback();
}
