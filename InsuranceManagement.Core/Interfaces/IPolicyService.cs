using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Models;

namespace InsuranceManagement.Core.Interfaces;

public interface IPolicyService
{
    Task<Policy> IssueAsync(
        Guid customerId,
        Policy policy,
        string? idempotencyKey = null,
        string? idempotencyFingerprint = null,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Policy>> GetAllAsync(
        PolicyType? type = null,
        PolicyStatus? status = null,
        Guid? customerId = null,
        int page = 1,
        int pageSize = 20,
        string sortBy = "createdAt",
        string sortDir = "desc",
        CancellationToken cancellationToken = default);

    Task<Policy> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PolicyEvent>> GetEventsAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<Policy> UpdateAsync(Guid id, Policy updates, CancellationToken cancellationToken = default);
    Task<Policy> CancelAsync(
        Guid id,
        string? idempotencyKey = null,
        string? idempotencyFingerprint = null,
        CancellationToken cancellationToken = default);
}
