using FluentAssertions;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Infrastructure.Data;
using InsuranceManagement.Infrastructure.Services;

namespace InsuranceManagement.Tests;

public class PolicyLifecycleTests
{
    [Fact]
    public async Task UpdateAsync_ChangesPremiumAndCoverage_WhenActive()
    {
        await using var db = TestDb.CreateContext();
        var policies = new PolicyService(db);
        var policy = await SeedActivePolicyAsync(db, policies);

        var updated = await policies.UpdateAsync(policy.Id, new Policy
        {
            Premium = 1500m,
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            CoverageDetails = "Updated coverage details"
        });

        updated.Premium.Should().Be(1500m);
        updated.CoverageDetails.Should().Be("Updated coverage details");
        updated.Status.Should().Be(PolicyStatus.Active);
    }

    [Fact]
    public async Task CancelAsync_SoftTerminatesPolicy()
    {
        await using var db = TestDb.CreateContext();
        var policies = new PolicyService(db);
        var policy = await SeedActivePolicyAsync(db, policies);

        var cancelled = await policies.CancelAsync(policy.Id);

        cancelled.Status.Should().Be(PolicyStatus.Cancelled);
        cancelled.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelAsync_IdempotencyRepeat_ReturnsSameCancelledPolicy()
    {
        await using var db = TestDb.CreateContext();
        var policies = new PolicyService(db);
        var policy = await SeedActivePolicyAsync(db, policies);

        var first = await policies.CancelAsync(policy.Id, "cancel-key", "same-fp");
        var second = await policies.CancelAsync(policy.Id, "cancel-key", "same-fp");

        second.Id.Should().Be(first.Id);
        second.Status.Should().Be(PolicyStatus.Cancelled);
    }

    [Fact]
    public async Task UpdateAsync_WhenCancelled_ThrowsBusinessRule()
    {
        await using var db = TestDb.CreateContext();
        var policies = new PolicyService(db);
        var policy = await SeedActivePolicyAsync(db, policies);
        await policies.CancelAsync(policy.Id);

        var act = async () => await policies.UpdateAsync(policy.Id, new Policy
        {
            Premium = 2000m,
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            CoverageDetails = "Should fail"
        });

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Cancelled policies cannot be updated*");
    }

    [Fact]
    public async Task CancelAsync_WhenAlreadyCancelled_ThrowsBusinessRule()
    {
        await using var db = TestDb.CreateContext();
        var policies = new PolicyService(db);
        var policy = await SeedActivePolicyAsync(db, policies);
        await policies.CancelAsync(policy.Id);

        var act = async () => await policies.CancelAsync(policy.Id);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*already cancelled*");
    }

    private static async Task<Policy> SeedActivePolicyAsync(InsuranceDbContext db, PolicyService policies)
    {
        var customers = new CustomerService(db);
        var customer = await customers.CreateAsync(new Customer
        {
            FullName = "Lifecycle User",
            Email = $"life-{Guid.NewGuid():N}@example.com",
            Phone = "+1-555-0400",
            DateOfBirth = new DateOnly(1980, 3, 3),
            Address = "4 Lifecycle Ave"
        });

        return await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Life,
            Premium = 800m,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CoverageDetails = "Lifecycle coverage"
        });
    }
}
