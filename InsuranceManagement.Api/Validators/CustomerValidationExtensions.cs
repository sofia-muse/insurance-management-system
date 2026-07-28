using FluentValidation;

namespace InsuranceManagement.Api.Validators;

internal static class CustomerValidationExtensions
{
    public static void ApplyCommonCustomerRules<T>(this AbstractValidator<T> validator, Func<T, DateOnly> dateOfBirthSelector)
    {
        validator.RuleFor(x => dateOfBirthSelector(x))
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage(ValidationConstants.DateOfBirthMustBeInPast)
            .Must(IsAdult)
            .WithMessage(ValidationConstants.CustomerMustBeAdult);
    }

    private static bool IsAdult(DateOnly dateOfBirth)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age >= 18;
    }
}
