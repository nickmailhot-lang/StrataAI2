namespace StrataAI.Domain.Organizations;

// PRD-27-FR-002. These categories do not grant governance or legal authority.
public static class OrganizationTypes
{
    public const string Default = "STRATA";
    public const string Legacy = "GENERIC";

    public static bool IsSupported(string? value) => value is
        "STRATA" or "HOA" or "CONDOMINIUM" or "COOPERATIVE" or
        "PROPERTY_MANAGEMENT_COMPANY" or "GENERIC";
}
