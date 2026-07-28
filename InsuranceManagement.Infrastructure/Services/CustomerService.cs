using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Core.Interfaces;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly InsuranceDbContext _db;

    public CustomerService(InsuranceDbContext db)
    {
        _db = db;
    }

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var emailExists = await _db.Customers.AnyAsync(
            c => c.Email.ToLower() == customer.Email.ToLower(),
            cancellationToken);

        if (emailExists)
        {
            throw new ConflictException($"A customer with email '{customer.Email}' already exists.");
        }

        var now = DateTime.UtcNow;
        customer.Id = Guid.NewGuid();
        customer.CreatedAt = now;
        customer.UpdatedAt = now;

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);
        return customer;
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.FullName.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term));
        }

        return await query
            .OrderBy(c => c.FullName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Customer> GetByIdAsync(Guid id, bool includePolicies = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Customer> query = _db.Customers.AsNoTracking();

        if (includePolicies)
        {
            query = query.Include(c => c.Policies);
        }

        var customer = await query.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            throw new NotFoundException($"Customer '{id}' was not found.");
        }

        return customer;
    }

    public async Task<Customer> UpdateAsync(Guid id, Customer updates, CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            throw new NotFoundException($"Customer '{id}' was not found.");
        }

        var emailTaken = await _db.Customers.AnyAsync(
            c => c.Id != id && c.Email.ToLower() == updates.Email.ToLower(),
            cancellationToken);

        if (emailTaken)
        {
            throw new ConflictException($"A customer with email '{updates.Email}' already exists.");
        }

        customer.FullName = updates.FullName;
        customer.Email = updates.Email;
        customer.Phone = updates.Phone;
        customer.DateOfBirth = updates.DateOfBirth;
        customer.Address = updates.Address;
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return customer;
    }
}
