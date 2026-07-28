using FluentValidation;

namespace InsuranceManagement.Api.Validators;

internal static class PolicyValidationExtensions
{
    public static void ApplyCommonPolicyTermRules<T>(this AbstractValidator<T> validator, Func<T, DateOnly> startDateSelector, Func<T, DateOnly> endDateSelector)
    {
        validator.RuleFor(x => endDateSelector(x))
            .GreaterThanOrEqualTo(x => startDateSelector(x))
            .WithMessage(ValidationConstants.PolicyEndDateOrderMessage);

        validator.RuleFor(x => endDateSelector(x))
            .LessThanOrEqualTo(x => startDateSelector(x).AddYears(50))
            .WithMessage(ValidationConstants.PolicyTermTooLongMessage);
    }
}
