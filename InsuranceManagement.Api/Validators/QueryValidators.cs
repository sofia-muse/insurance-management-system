using FluentValidation;
using InsuranceManagement.Api.DTOs;

namespace InsuranceManagement.Api.Validators;

public class CustomerQueryRequestValidator : AbstractValidator<CustomerQueryRequest>
{
    private static readonly string[] AllowedSortBy = ["fullName", "email", "createdAt", "updatedAt"];
    public CustomerQueryRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy)
            .NotEmpty()
            .Must(v => AllowedSortBy.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortBy must be one of: fullName, email, createdAt, updatedAt.");
        RuleFor(x => x.SortDir)
            .NotEmpty()
            .Must(v => ValidationConstants.AllowedSortDir.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortDir must be either 'asc' or 'desc'.");
        RuleFor(x => x.Search)
            .MaximumLength(100)
            .WithMessage("Search must be 100 characters or fewer.");
    }
}

public class PolicyQueryRequestValidator : AbstractValidator<PolicyQueryRequest>
{
    private static readonly string[] AllowedSortBy = ["createdAt", "premium", "startDate", "endDate", "status"];
    private static readonly string[] AllowedType = ["Car", "Health", "Life"];
    private static readonly string[] AllowedStatus = ["Active", "Cancelled", "Expired"];

    public PolicyQueryRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy)
            .NotEmpty()
            .Must(v => AllowedSortBy.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortBy must be one of: createdAt, premium, startDate, endDate, status.");
        RuleFor(x => x.SortDir)
            .NotEmpty()
            .Must(v => ValidationConstants.AllowedSortDir.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("SortDir must be either 'asc' or 'desc'.");
        RuleFor(x => x.Type)
            .Must(v => string.IsNullOrWhiteSpace(v) || AllowedType.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be one of: Car, Health, Life.");
        RuleFor(x => x.Status)
            .Must(v => string.IsNullOrWhiteSpace(v) || AllowedStatus.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Status must be one of: Active, Cancelled, Expired.");
    }
}
