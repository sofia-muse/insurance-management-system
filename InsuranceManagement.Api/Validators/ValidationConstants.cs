namespace InsuranceManagement.Api.Validators;

internal static class ValidationConstants
{
    public static readonly string[] AllowedSortDir = ["asc", "desc"];
    public const string DateOfBirthMustBeInPast = "DateOfBirth must be in the past.";
    public const string CustomerMustBeAdult = "Customer must be at least 18 years old.";
    public const string PolicyEndDateOrderMessage = "EndDate must be on or after StartDate.";
    public const string PolicyTermTooLongMessage = "Policy term is unrealistically long.";
}
