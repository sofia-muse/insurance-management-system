using InsuranceManagement.Api.DTOs;
using InsuranceManagement.Core.Entities;

namespace InsuranceManagement.Api.DTOs;

public static class MappingExtensions
{
    public static CustomerResponse ToResponse(this Customer customer, bool includePolicies = false)
    {
        return new CustomerResponse(
            customer.Id,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.DateOfBirth,
            customer.Address,
            customer.CreatedAt,
            customer.UpdatedAt,
            includePolicies
                ? customer.Policies
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => new PolicySummaryResponse(
                        p.Id,
                        p.PolicyNumber,
                        p.Type.ToString(),
                        p.Status.ToString(),
                        p.Premium))
                    .ToList()
                : null);
    }

    public static PolicyResponse ToResponse(this Policy policy)
    {
        return new PolicyResponse(
            policy.Id,
            policy.CustomerId,
            policy.PolicyNumber,
            policy.Type.ToString(),
            policy.Status.ToString(),
            policy.Premium,
            policy.StartDate,
            policy.EndDate,
            policy.CoverageDetails,
            policy.CreatedAt,
            policy.UpdatedAt,
            policy.CancelledAt);
    }
}
