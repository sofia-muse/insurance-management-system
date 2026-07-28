using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;

namespace InsuranceManagement.Core.Interfaces;

public interface IPolicyService
{
    Task<Policy> IssueAsync(Guid customerId, Policy policy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Policy>> GetAllAsync(PolicyType? type = null, PolicyStatus? status = null, Guid? customerId = null, CancellationToken cancellationToken = default);
    Task<Policy> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Policy> UpdateAsync(Guid id, Policy updates, CancellationToken cancellationToken = default);
    Task<Policy> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}
