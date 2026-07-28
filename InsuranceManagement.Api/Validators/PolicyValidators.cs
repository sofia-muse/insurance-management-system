using FluentValidation;
using InsuranceManagement.Api.DTOs;

namespace InsuranceManagement.Api.Validators;

public class IssuePolicyRequestValidator : AbstractValidator<IssuePolicyRequest>
{
    public IssuePolicyRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Premium).InclusiveBetween(1m, 1_000_000m);
        RuleFor(x => x.CoverageDetails).NotEmpty().MaximumLength(2000);
        this.ApplyCommonPolicyTermRules(x => x.StartDate, x => x.EndDate);

        RuleFor(x => x.StartDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1))
            .WithMessage("StartDate is unrealistically old.");
    }
}

public class UpdatePolicyRequestValidator : AbstractValidator<UpdatePolicyRequest>
{
    public UpdatePolicyRequestValidator()
    {
        RuleFor(x => x.Premium).InclusiveBetween(1m, 1_000_000m);
        RuleFor(x => x.CoverageDetails).NotEmpty().MaximumLength(2000);
        this.ApplyCommonPolicyTermRules(x => x.StartDate, x => x.EndDate);
    }
}
