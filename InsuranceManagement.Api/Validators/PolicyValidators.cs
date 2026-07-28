using FluentValidation;
using InsuranceManagement.Api.DTOs;

namespace InsuranceManagement.Api.Validators;

public class IssuePolicyRequestValidator : AbstractValidator<IssuePolicyRequest>
{
    public IssuePolicyRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Premium).GreaterThan(0);
        RuleFor(x => x.CoverageDetails).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}

public class UpdatePolicyRequestValidator : AbstractValidator<UpdatePolicyRequest>
{
    public UpdatePolicyRequestValidator()
    {
        RuleFor(x => x.Premium).GreaterThan(0);
        RuleFor(x => x.CoverageDetails).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
    }
}
