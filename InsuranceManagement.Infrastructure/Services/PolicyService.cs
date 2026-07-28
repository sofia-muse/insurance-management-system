using System.Text.Json;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Core.Interfaces;
using InsuranceManagement.Core.Models;
using InsuranceManagement.Core.Utilities;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Infrastructure.Services;

// Core policy domain orchestration.
// Enforces business invariants, writes audit events, and handles idempotent
// retries/concurrency edge cases around persistence.
public class PolicyService : IPolicyService
{
    private const string CustomerNotFoundCode = "CUSTOMER_NOT_FOUND";
    private const string PolicyNotFoundCode = "POLICY_NOT_FOUND";
    private const string ActiveCarPolicyExistsCode = "ACTIVE_CAR_POLICY_EXISTS";
    private const string PolicyConcurrentModificationCode = "POLICY_CONCURRENT_MODIFICATION";
    private readonly InsuranceDbContext _db;

    public PolicyService(InsuranceDbContext db)
    {
        _db = db;
    }

    public async Task<Policy> IssueAsync(
        Guid customerId,
        Policy policy,
        string? idempotencyKey = null,
        string? idempotencyFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyInputs(idempotencyKey, idempotencyFingerprint);
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            throw CustomerNotFound(customerId);
        }

        var operation = $"issue:{customerId}";
        var existingIdempotency = await GetIdempotencyAsync(operation, idempotencyKey, cancellationToken);
        if (existingIdempotency is not null)
        {
            // Same operation+key should return the original result unless payload changed.
            ValidateFingerprint(existingIdempotency, idempotencyFingerprint);
            return await GetByIdAsync(existingIdempotency.ResponseEntityId, cancellationToken);
        }

        ValidateDates(policy.StartDate, policy.EndDate);

        if (policy.Type == PolicyType.Car)
        {
            await EnsureNoActiveCarPolicyConflictAsync(customerId, excludePolicyId: null, cancellationToken);
        }

        var now = DateTime.UtcNow;
        policy.Id = Guid.NewGuid();
        policy.CustomerId = customerId;
        policy.PolicyNumber = GeneratePolicyNumber(policy.Type);
        policy.Status = ResolveStatus(policy.EndDate, PolicyStatus.Active);
        policy.CreatedAt = now;
        policy.UpdatedAt = now;
        policy.CancelledAt = null;

        _db.Policies.Add(policy);
        AddPolicyEvent(policy, PolicyEventType.Issued, now);
        AddIdempotencyRecord(operation, idempotencyKey, idempotencyFingerprint, policy.Id, now);
        var replayEntityId = await SaveChangesWithIdempotencyRaceHandlingAsync(
            operation,
            idempotencyKey,
            idempotencyFingerprint,
            cancellationToken);
        if (replayEntityId.HasValue)
        {
            return await GetByIdAsync(replayEntityId.Value, cancellationToken);
        }

        return policy;
    }

    public async Task<PagedResult<Policy>> GetAllAsync(
        PolicyType? type = null,
        PolicyStatus? status = null,
        Guid? customerId = null,
        int page = 1,
        int pageSize = 20,
        string sortBy = "createdAt",
        string sortDir = "desc",
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = ServiceQueryHelpers.NormalizePaging(page, pageSize);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.Policies.AsNoTracking().AsQueryable();

        if (type.HasValue)
        {
            query = query.Where(p => p.Type == type.Value);
        }

        if (status.HasValue)
        {
            query = status.Value switch
            {
                PolicyStatus.Active => query.Where(p => p.Status == PolicyStatus.Active && p.EndDate >= today),
                PolicyStatus.Expired => query.Where(p => p.Status == PolicyStatus.Expired || (p.Status == PolicyStatus.Active && p.EndDate < today)),
                _ => query.Where(p => p.Status == status.Value)
            };
        }

        if (customerId.HasValue)
        {
            query = query.Where(p => p.CustomerId == customerId.Value);
        }

        var isDesc = ServiceQueryHelpers.IsDescending(sortDir);
        query = (sortBy ?? "createdAt").ToLowerInvariant() switch
        {
            "premium" => isDesc ? query.OrderByDescending(p => p.Premium) : query.OrderBy(p => p.Premium),
            "startdate" => isDesc ? query.OrderByDescending(p => p.StartDate) : query.OrderBy(p => p.StartDate),
            "enddate" => isDesc ? query.OrderByDescending(p => p.EndDate) : query.OrderBy(p => p.EndDate),
            "status" => isDesc
                ? query.OrderByDescending(p => p.Status == PolicyStatus.Cancelled ? (int)PolicyStatus.Cancelled : p.EndDate < today ? (int)PolicyStatus.Expired : (int)p.Status)
                : query.OrderBy(p => p.Status == PolicyStatus.Cancelled ? (int)PolicyStatus.Cancelled : p.EndDate < today ? (int)PolicyStatus.Expired : (int)p.Status),
            _ => isDesc ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt)
        };

        return await ServiceQueryHelpers.ToPagedResultAsync(
            query,
            page,
            pageSize,
            cancellationToken);
    }

    public async Task<Policy> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetPolicyOrThrowAsync(_db.Policies.AsNoTracking(), id, cancellationToken);
    }

    public async Task<IReadOnlyList<PolicyEvent>> GetEventsAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.Policies.AsNoTracking().AnyAsync(p => p.Id == policyId, cancellationToken);
        if (!exists)
        {
            throw PolicyNotFound(policyId);
        }

        return await _db.PolicyEvents.AsNoTracking()
            .Where(e => e.PolicyId == policyId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Policy> UpdateAsync(Guid id, Policy updates, CancellationToken cancellationToken = default)
    {
        var policy = await GetPolicyOrThrowAsync(_db.Policies, id, cancellationToken);

        if (policy.Status == PolicyStatus.Cancelled)
        {
            throw new BusinessRuleException("POLICY_CANCELLED_UPDATE_FORBIDDEN", "Cancelled policies cannot be updated.");
        }

        ValidateDates(updates.StartDate, updates.EndDate);

        policy.Premium = updates.Premium;
        policy.StartDate = updates.StartDate;
        policy.EndDate = updates.EndDate;
        policy.CoverageDetails = updates.CoverageDetails;
        policy.Status = ResolveStatus(updates.EndDate, PolicyStatus.Active);
        policy.UpdatedAt = DateTime.UtcNow;

        if (policy.Type == PolicyType.Car && policy.Status == PolicyStatus.Active)
        {
            await EnsureNoActiveCarPolicyConflictAsync(policy.CustomerId, excludePolicyId: policy.Id, cancellationToken);
        }

        AddPolicyEvent(policy, PolicyEventType.Updated, policy.UpdatedAt);
        await SaveChangesWithPolicyConflictHandlingAsync(cancellationToken);
        return policy;
    }

    public async Task<Policy> CancelAsync(
        Guid id,
        string? idempotencyKey = null,
        string? idempotencyFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyInputs(idempotencyKey, idempotencyFingerprint);
        var operation = $"cancel:{id}";
        var existingIdempotency = await GetIdempotencyAsync(operation, idempotencyKey, cancellationToken);
        if (existingIdempotency is not null)
        {
            ValidateFingerprint(existingIdempotency, idempotencyFingerprint);
            return await GetByIdAsync(existingIdempotency.ResponseEntityId, cancellationToken);
        }

        var policy = await GetPolicyOrThrowAsync(_db.Policies, id, cancellationToken);

        if (policy.Status == PolicyStatus.Cancelled)
        {
            throw new BusinessRuleException("POLICY_ALREADY_CANCELLED", "Policy is already cancelled.");
        }

        var now = DateTime.UtcNow;
        policy.Status = PolicyStatus.Cancelled;
        policy.CancelledAt = now;
        policy.UpdatedAt = now;

        AddPolicyEvent(policy, PolicyEventType.Cancelled, now);
        AddIdempotencyRecord(operation, idempotencyKey, idempotencyFingerprint, policy.Id, now);
        var replayEntityId = await SaveChangesWithIdempotencyRaceHandlingAsync(
            operation,
            idempotencyKey,
            idempotencyFingerprint,
            cancellationToken);
        if (replayEntityId.HasValue)
        {
            return await GetByIdAsync(replayEntityId.Value, cancellationToken);
        }

        return policy;
    }

    private async Task<Guid?> SaveChangesWithIdempotencyRaceHandlingAsync(
        string operation,
        string? idempotencyKey,
        string? idempotencyFingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException ex) when (
            operation.StartsWith("issue:", StringComparison.OrdinalIgnoreCase)
            && IsStaleCarNormalizationConcurrency(ex))
        {
            // Concurrent writer may already have expired/updated the stale car.
            // Reload, re-apply expiration if still needed, then retry once with
            // the same conflict mapping as the primary save path.
            await RetryStaleCarNormalizationAsync(ex, cancellationToken);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw PolicyConcurrentModification();
            }
            catch (DbUpdateException retryEx) when (IsActiveCarUniqueConstraintViolation(retryEx))
            {
                throw ActiveCarPolicyExists();
            }
            catch (DbUpdateException retryEx) when (
                !string.IsNullOrWhiteSpace(idempotencyKey) &&
                IsIdempotencyUniqueConstraintViolation(retryEx))
            {
                var existing = await GetIdempotencyAsync(operation, idempotencyKey, cancellationToken);
                if (existing is null)
                {
                    throw;
                }

                ValidateFingerprint(existing, idempotencyFingerprint);
                _db.ChangeTracker.Clear();
                return existing.ResponseEntityId;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            throw PolicyConcurrentModification();
        }
        catch (DbUpdateException ex) when (IsActiveCarUniqueConstraintViolation(ex))
        {
            throw ActiveCarPolicyExists();
        }
        catch (DbUpdateException ex) when (
            !string.IsNullOrWhiteSpace(idempotencyKey) &&
            IsIdempotencyUniqueConstraintViolation(ex))
        {
            // Concurrent insert using same idempotency key won the race:
            // reload winner row and replay the original entity response.
            var existing = await GetIdempotencyAsync(operation, idempotencyKey, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            ValidateFingerprint(existing, idempotencyFingerprint);
            _db.ChangeTracker.Clear();
            return existing.ResponseEntityId;
        }
    }

    private async Task<IdempotencyRecord?> GetIdempotencyAsync(string operation, string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return await _db.IdempotencyRecords.FirstOrDefaultAsync(
            i => i.Operation == operation && i.Key == key,
            cancellationToken);
    }

    private static void ValidateFingerprint(IdempotencyRecord existing, string? requestFingerprint)
    {
        if (string.IsNullOrWhiteSpace(requestFingerprint) || existing.Fingerprint != requestFingerprint)
        {
            throw new ConflictException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD", "Idempotency key was already used with a different payload.");
        }
    }

    private static bool IsIdempotencyUniqueConstraintViolation(DbUpdateException ex)
        => ServiceQueryHelpers.IsUniqueConstraintViolation(ex, "UX_IdempotencyRecord_Operation_Key");

    private static bool IsActiveCarUniqueConstraintViolation(DbUpdateException ex)
        => ServiceQueryHelpers.IsUniqueConstraintViolation(ex, "UX_Policy_OneActiveCarPerCustomer");

    private static bool IsStaleCarNormalizationConcurrency(DbUpdateConcurrencyException ex)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return ex.Entries.All(entry =>
            entry.Entity is Policy p
            && entry.State == EntityState.Modified
            && p.Type == PolicyType.Car
            && p.EndDate < today);
    }

    private async Task RetryStaleCarNormalizationAsync(DbUpdateConcurrencyException ex, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        foreach (var entry in ex.Entries)
        {
            await entry.ReloadAsync(cancellationToken);
            if (entry.Entity is not Policy policy)
            {
                continue;
            }

            // Drop the pre-failure pending Updated event so a successful retry
            // records at most one audit event for this logical expiration.
            foreach (var pendingEvent in _db.ChangeTracker.Entries<PolicyEvent>()
                .Where(e => e.State == EntityState.Added && e.Entity.PolicyId == policy.Id)
                .ToList())
            {
                pendingEvent.State = EntityState.Detached;
            }

            // Another request may already have expired the row; only re-apply
            // expiration when the reloaded row is still a stale active car.
            if (policy.Type == PolicyType.Car
                && policy.Status == PolicyStatus.Active
                && policy.EndDate < today)
            {
                policy.Status = PolicyStatus.Expired;
                policy.UpdatedAt = now;
                AddPolicyEvent(policy, PolicyEventType.Updated, now);
            }
        }
    }

    private async Task SaveChangesWithPolicyConflictHandlingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw PolicyConcurrentModification();
        }
        catch (DbUpdateException ex) when (IsActiveCarUniqueConstraintViolation(ex))
        {
            throw ActiveCarPolicyExists();
        }
    }

    private async Task EnsureNoActiveCarPolicyConflictAsync(Guid customerId, Guid? excludePolicyId, CancellationToken cancellationToken)
    {
        // Normalize stale "active" rows first so invariant checks reflect
        // current business time semantics before issuing/updating a car policy.
        await NormalizeExpiredActiveCarPoliciesAsync(customerId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hasActiveCar = await _db.Policies.AnyAsync(
            p => p.CustomerId == customerId
                && p.Type == PolicyType.Car
                && p.Status == PolicyStatus.Active
                && p.EndDate >= today
                && (!excludePolicyId.HasValue || p.Id != excludePolicyId.Value),
            cancellationToken);

        if (hasActiveCar)
        {
            throw ActiveCarPolicyExists();
        }
    }

    private static NotFoundException CustomerNotFound(Guid id)
        => new(CustomerNotFoundCode, $"Customer '{id}' was not found.");

    private static NotFoundException PolicyNotFound(Guid id)
        => new(PolicyNotFoundCode, $"Policy '{id}' was not found.");

    private static ConflictException ActiveCarPolicyExists()
        => new(ActiveCarPolicyExistsCode, "Customer already has an active Car policy.");

    private static ConflictException PolicyConcurrentModification()
        => new(PolicyConcurrentModificationCode, "Policy was modified by another request. Reload and retry.");

    private static async Task<Policy> GetPolicyOrThrowAsync(
        IQueryable<Policy> query,
        Guid id,
        CancellationToken cancellationToken)
    {
        var policy = await query.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null)
        {
            throw PolicyNotFound(id);
        }

        return policy;
    }

    private async Task NormalizeExpiredActiveCarPoliciesAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var staleActiveCars = await _db.Policies
            .Where(p => p.CustomerId == customerId
                && p.Type == PolicyType.Car
                && p.Status == PolicyStatus.Active
                && p.EndDate < today)
            .ToListAsync(cancellationToken);

        if (staleActiveCars.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var stalePolicy in staleActiveCars)
        {
            stalePolicy.Status = PolicyStatus.Expired;
            stalePolicy.UpdatedAt = now;
            AddPolicyEvent(stalePolicy, PolicyEventType.Updated, now);
        }
    }

    private static void ValidateIdempotencyInputs(string? idempotencyKey, string? idempotencyFingerprint)
    {
        var hasKey = !string.IsNullOrWhiteSpace(idempotencyKey);
        var hasFingerprint = !string.IsNullOrWhiteSpace(idempotencyFingerprint);
        if (hasKey == hasFingerprint)
        {
            return;
        }

        throw new BusinessRuleException(
            "IDEMPOTENCY_INPUT_MISMATCH",
            "Idempotency key and fingerprint must both be provided together.");
    }

    private void AddIdempotencyRecord(string operation, string? key, string? fingerprint, Guid entityId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Operation = operation,
            Key = key,
            Fingerprint = fingerprint,
            ResponseEntityId = entityId,
            CreatedAt = now
        });
    }

    private void AddPolicyEvent(Policy policy, PolicyEventType eventType, DateTime occurredAt)
    {
        // Snapshot captures business-visible policy state at event time to
        // keep an immutable and queryable audit trail.
        var snapshot = JsonSerializer.Serialize(new
        {
            policy.Id,
            policy.CustomerId,
            policy.PolicyNumber,
            Type = policy.Type.ToString(),
            Status = policy.Status.ToString(),
            policy.Premium,
            policy.StartDate,
            policy.EndDate,
            policy.CoverageDetails,
            policy.CancelledAt
        });

        _db.PolicyEvents.Add(new PolicyEvent
        {
            Id = Guid.NewGuid(),
            PolicyId = policy.Id,
            CustomerId = policy.CustomerId,
            EventType = eventType,
            OccurredAt = occurredAt,
            SnapshotJson = snapshot
        });
    }

    private static void ValidateDates(DateOnly startDate, DateOnly endDate)
    {
        if (startDate > endDate)
        {
            throw new BusinessRuleException("INVALID_POLICY_DATES", "StartDate must be on or before EndDate.");
        }
    }

    private static PolicyStatus ResolveStatus(DateOnly endDate, PolicyStatus fallback)
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
