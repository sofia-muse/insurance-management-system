using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Core.Interfaces;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Infrastructure.Services;

public class PolicyService : IPolicyService
{
    private readonly InsuranceDbContext _db;

    public PolicyService(InsuranceDbContext db)
    {
        _db = db;
    }

    public async Task<Policy> IssueAsync(Guid customerId, Policy policy, CancellationToken cancellationToken = default)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            throw new NotFoundException($"Customer '{customerId}' was not found.");
        }

        ValidateDates(policy.StartDate, policy.EndDate);

        if (policy.Type == PolicyType.Car)
        {
            var hasActiveCar = await _db.Policies.AnyAsync(
                p => p.CustomerId == customerId &&
                     p.Type == PolicyType.Car &&
                     p.Status == PolicyStatus.Active,
                cancellationToken);

            if (hasActiveCar)
            {
                throw new ConflictException("Customer already has an active Car policy.");
            }
        }

        var now = DateTime.UtcNow;
        policy.Id = Guid.NewGuid();
        policy.CustomerId = customerId;
        policy.PolicyNumber = GeneratePolicyNumber(policy.Type);
        policy.Status = ResolveStatus(policy.StartDate, policy.EndDate, PolicyStatus.Active);
        policy.CreatedAt = now;
        policy.UpdatedAt = now;
        policy.CancelledAt = null;

        _db.Policies.Add(policy);
        await _db.SaveChangesAsync(cancellationToken);
        return policy;
    }

    public async Task<IReadOnlyList<Policy>> GetAllAsync(
        PolicyType? type = null,
        PolicyStatus? status = null,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Policies.AsNoTracking().AsQueryable();

        if (type.HasValue)
        {
            query = query.Where(p => p.Type == type.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(p => p.CustomerId == customerId.Value);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Policy> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var policy = await _db.Policies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (policy is null)
        {
            throw new NotFoundException($"Policy '{id}' was not found.");
        }

        return policy;
    }

    public async Task<Policy> UpdateAsync(Guid id, Policy updates, CancellationToken cancellationToken = default)
    {
        var policy = await _db.Policies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            throw new NotFoundException($"Policy '{id}' was not found.");
        }

        if (policy.Status == PolicyStatus.Cancelled)
        {
            throw new BusinessRuleException("Cancelled policies cannot be updated.");
        }

        ValidateDates(updates.StartDate, updates.EndDate);

        policy.Premium = updates.Premium;
        policy.StartDate = updates.StartDate;
        policy.EndDate = updates.EndDate;
        policy.CoverageDetails = updates.CoverageDetails;
        policy.Status = ResolveStatus(updates.StartDate, updates.EndDate, PolicyStatus.Active);
        policy.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return policy;
    }

    public async Task<Policy> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var policy = await _db.Policies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            throw new NotFoundException($"Policy '{id}' was not found.");
        }

        if (policy.Status == PolicyStatus.Cancelled)
        {
            throw new BusinessRuleException("Policy is already cancelled.");
        }

        var now = DateTime.UtcNow;
        policy.Status = PolicyStatus.Cancelled;
        policy.CancelledAt = now;
        policy.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private static void ValidateDates(DateOnly startDate, DateOnly endDate)
    {
        if (startDate > endDate)
        {
            throw new BusinessRuleException("StartDate must be on or before EndDate.");
        }
    }

    private static PolicyStatus ResolveStatus(DateOnly startDate, DateOnly endDate, PolicyStatus fallback)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (endDate < today)
        {
            return PolicyStatus.Expired;
        }

        return fallback;
    }

    private static string GeneratePolicyNumber(PolicyType type)
    {
        var shortId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"POL-{type.ToString().ToUpperInvariant()}-{shortId}";
    }
}
