using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Core.Interfaces;
using InsuranceManagement.Core.Models;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private const string CustomerNotFoundCode = "CUSTOMER_NOT_FOUND";
    private const string CustomerEmailExistsCode = "CUSTOMER_EMAIL_EXISTS";
    private readonly InsuranceDbContext _db;

    public CustomerService(InsuranceDbContext db)
    {
        _db = db;
    }

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(customer.Email);
        var emailExists = await _db.Customers.AnyAsync(c => c.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            throw CustomerEmailExists(customer.Email);
        }

        var now = DateTime.UtcNow;
        customer.Id = Guid.NewGuid();
        customer.Email = normalizedEmail;
        customer.CreatedAt = now;
        customer.UpdatedAt = now;

        _db.Customers.Add(customer);
        await SaveChangesWithEmailConflictHandlingAsync(customer.Email, cancellationToken);

        return customer;
    }

    public async Task<PagedResult<Customer>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        string sortBy = "fullName",
        string sortDir = "asc",
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = ServiceQueryHelpers.NormalizePaging(page, pageSize);

        var query = _db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c => c.FullName.ToLower().Contains(term) || c.Email.ToLower().Contains(term));
        }

        var isDesc = ServiceQueryHelpers.IsDescending(sortDir);
        query = (sortBy ?? "fullName").ToLowerInvariant() switch
        {
            "createdat" => isDesc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            "updatedat" => isDesc ? query.OrderByDescending(c => c.UpdatedAt) : query.OrderBy(c => c.UpdatedAt),
            "email" => isDesc ? query.OrderByDescending(c => c.Email) : query.OrderBy(c => c.Email),
            _ => isDesc ? query.OrderByDescending(c => c.FullName) : query.OrderBy(c => c.FullName)
        };

        return await ServiceQueryHelpers.ToPagedResultAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<Customer> GetByIdAsync(Guid id, bool includePolicies = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Customer> query = _db.Customers.AsNoTracking();
        if (includePolicies)
        {
            query = query.Include(c => c.Policies);
        }

        return await GetCustomerOrThrowAsync(query, id, cancellationToken);
    }

    public async Task<Customer> UpdateAsync(Guid id, Customer updates, CancellationToken cancellationToken = default)
    {
        var customer = await GetCustomerOrThrowAsync(_db.Customers, id, cancellationToken);

        var normalizedEmail = NormalizeEmail(updates.Email);
        var emailTaken = await _db.Customers.AnyAsync(c => c.Id != id && c.Email.ToLower() == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            throw CustomerEmailExists(updates.Email);
        }

        customer.FullName = updates.FullName;
        customer.Email = normalizedEmail;
        customer.Phone = updates.Phone;
        customer.DateOfBirth = updates.DateOfBirth;
        customer.Address = updates.Address;
        customer.UpdatedAt = DateTime.UtcNow;

        await SaveChangesWithEmailConflictHandlingAsync(updates.Email, cancellationToken);

        return customer;
    }

    private static bool IsCustomerEmailUniqueConstraintViolation(DbUpdateException ex)
        => ServiceQueryHelpers.IsUniqueConstraintViolation(ex, "IX_Customers_Email");

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static ConflictException CustomerEmailExists(string email)
        => new(CustomerEmailExistsCode, $"A customer with email '{email}' already exists.");

    private static NotFoundException CustomerNotFound(Guid id)
        => new(CustomerNotFoundCode, $"Customer '{id}' was not found.");

    private async Task SaveChangesWithEmailConflictHandlingAsync(string emailForMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsCustomerEmailUniqueConstraintViolation(ex))
        {
            throw CustomerEmailExists(emailForMessage);
        }
    }

    private static async Task<Customer> GetCustomerOrThrowAsync(
        IQueryable<Customer> query,
        Guid id,
        CancellationToken cancellationToken)
    {
        var customer = await query.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            throw CustomerNotFound(id);
        }

        return customer;
    }
}
