using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Models;

namespace InsuranceManagement.Core.Interfaces;

public interface ICustomerService
{
    Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default);
    Task<PagedResult<Customer>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        string sortBy = "fullName",
        string sortDir = "asc",
        CancellationToken cancellationToken = default);
    Task<Customer> GetByIdAsync(Guid id, bool includePolicies = false, CancellationToken cancellationToken = default);
    Task<Customer> UpdateAsync(Guid id, Customer updates, CancellationToken cancellationToken = default);
}
