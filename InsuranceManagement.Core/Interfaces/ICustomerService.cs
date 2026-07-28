using InsuranceManagement.Core.Entities;

namespace InsuranceManagement.Core.Interfaces;

public interface ICustomerService
{
    Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default);
    Task<Customer> GetByIdAsync(Guid id, bool includePolicies = false, CancellationToken cancellationToken = default);
    Task<Customer> UpdateAsync(Guid id, Customer updates, CancellationToken cancellationToken = default);
}
