using InsuranceManagement.Core.Enums;

namespace InsuranceManagement.Api.DTOs;

public record IssuePolicyRequest(
    PolicyType Type,
    decimal Premium,
    DateOnly StartDate,
    DateOnly EndDate,
    string CoverageDetails);

public record UpdatePolicyRequest(
    decimal Premium,
    DateOnly StartDate,
    DateOnly EndDate,
    string CoverageDetails);

public record PolicyResponse(
    Guid Id,
    Guid CustomerId,
    string PolicyNumber,
    string Type,
    string Status,
    decimal Premium,
    DateOnly StartDate,
    DateOnly EndDate,
    string CoverageDetails,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CancelledAt);
