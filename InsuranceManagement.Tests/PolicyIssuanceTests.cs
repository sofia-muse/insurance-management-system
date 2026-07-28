using FluentAssertions;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Tests;

public class PolicyIssuanceTests
{
    [Fact]
    public async Task IssueAsync_LinksPolicyToExistingCustomer()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());

        var policy = await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health));

        policy.CustomerId.Should().Be(customer.Id);
        policy.PolicyNumber.Should().StartWith("POL-HEALTH-");
        policy.Status.Should().Be(PolicyStatus.Active);

        var events = await policies.GetEventsAsync(policy.Id);
        events.Should().ContainSingle(e => e.EventType == PolicyEventType.Issued);
    }

    [Fact]
    public async Task IssueAsync_WithSameIdempotencyKeyAndPayload_ReturnsSamePolicy()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());

        var key = "issue-key-1";
        var fingerprint = "fp-1";

        var first = await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health), key, fingerprint);
        var second = await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health), key, fingerprint);

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task IssueAsync_WithSameIdempotencyKeyAndDifferentPayload_ThrowsConflict()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());

        await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health), "issue-key-2", "fp-a");

        var act = async () => await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health), "issue-key-2", "fp-b");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*Idempotency key*different payload*");
    }

    [Fact]
    public async Task IssueAsync_WhenIdempotencySaveCollides_ReturnsStoredPolicy()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());

        var key = "issue-race-key";
        var fingerprint = "race-fingerprint";

        // Simulate another request having already persisted this idempotency result.
        var existingPolicyId = Guid.NewGuid();
        db.Policies.Add(new Policy
        {
            Id = existingPolicyId,
            CustomerId = customer.Id,
            PolicyNumber = "POL-HEALTH-RACE01",
            Type = PolicyType.Health,
            Status = PolicyStatus.Active,
            Premium = 333m,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CoverageDetails = "Race winner policy",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Operation = $"issue:{customer.Id}",
            Key = key,
            Fingerprint = fingerprint,
            ResponseEntityId = existingPolicyId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var replayed = await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health), key, fingerprint);
        replayed.Id.Should().Be(existingPolicyId);
        replayed.PolicyNumber.Should().Be("POL-HEALTH-RACE01");
    }

    [Fact]
    public async Task IssueAsync_WhenCustomerMissing_ThrowsNotFound()
    {
        await using var db = TestDb.CreateContext();
        var policies = new PolicyService(db);

        var act = async () => await policies.IssueAsync(Guid.NewGuid(), NewPolicy(PolicyType.Life));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task IssueAsync_WhenActiveCarAlreadyExists_ThrowsConflict()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());
        await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Car));

        var act = async () => await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Car));

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*active Car policy*");
    }

    [Fact]
    public async Task IssueAsync_WhenStartAfterEnd_ThrowsBusinessRule()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());

        var invalid = NewPolicy(PolicyType.Life);
        invalid.StartDate = new DateOnly(2027, 1, 1);
        invalid.EndDate = new DateOnly(2026, 1, 1);

        var act = async () => await policies.IssueAsync(customer.Id, invalid);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task IssueAsync_WithIdempotencyKeyWithoutFingerprint_ThrowsBusinessRule()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());

        var act = async () => await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health), "issue-key-only", null);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Idempotency key and fingerprint must both be provided together*");
    }

    [Fact]
    public async Task IssueAsync_NormalizesStaleActiveCarAndWritesUpdateAuditEvent()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var staleCar = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Car,
            Premium = 410m,
            StartDate = today.AddDays(-30),
            EndDate = today.AddDays(-2),
            CoverageDetails = "Stale car policy"
        });

        var storedStale = await db.Policies.FirstAsync(p => p.Id == staleCar.Id);
        storedStale.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();

        var newCar = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Car,
            Premium = 520m,
            StartDate = today,
            EndDate = today.AddDays(180),
            CoverageDetails = "Replacement car policy"
        });

        newCar.Status.Should().Be(PolicyStatus.Active);
        var refreshedStale = await db.Policies.FirstAsync(p => p.Id == staleCar.Id);
        refreshedStale.Status.Should().Be(PolicyStatus.Expired);

        var staleEvents = await policies.GetEventsAsync(staleCar.Id);
        staleEvents.Should().Contain(e => e.EventType == PolicyEventType.Updated);
    }

    private static Customer NewCustomer() => new()
    {
        FullName = "Policy Holder",
        Email = $"holder-{Guid.NewGuid():N}@example.com",
        Phone = "+1-555-0200",
        DateOfBirth = new DateOnly(1985, 5, 5),
        Address = "2 Policy Lane"
    };

    private static Policy NewPolicy(PolicyType type) => new()
    {
        Type = type,
        Premium = 1000m,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
        CoverageDetails = "Standard coverage"
    };
}
