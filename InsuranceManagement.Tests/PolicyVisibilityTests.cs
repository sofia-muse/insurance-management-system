using FluentAssertions;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Tests;

public class PolicyVisibilityTests
{
    [Fact]
    public async Task GetAllAsync_FiltersByTypeCustomerAndStatus()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);

        var alice = await customers.CreateAsync(NewCustomer("alice@example.com"));
        var bob = await customers.CreateAsync(NewCustomer("bob@example.com"));

        await policies.IssueAsync(alice.Id, NewPolicy(PolicyType.Car));
        await policies.IssueAsync(alice.Id, NewPolicy(PolicyType.Health));
        var bobLife = await policies.IssueAsync(bob.Id, NewPolicy(PolicyType.Life));
        await policies.CancelAsync(bobLife.Id);

        var cars = await policies.GetAllAsync(type: PolicyType.Car, page: 1, pageSize: 20);
        var alicePolicies = await policies.GetAllAsync(customerId: alice.Id, page: 1, pageSize: 20);
        var cancelled = await policies.GetAllAsync(status: PolicyStatus.Cancelled, page: 1, pageSize: 20);

        cars.Items.Should().ContainSingle(p => p.Type == PolicyType.Car);
        alicePolicies.Items.Should().HaveCount(2);
        cancelled.Items.Should().ContainSingle(p => p.Id == bobLife.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsIssuedPolicy()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer("viewer@example.com"));
        var issued = await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health));

        var found = await policies.GetByIdAsync(issued.Id);

        found.Id.Should().Be(issued.Id);
        found.Type.Should().Be(PolicyType.Health);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsPersistedStatusWithoutMutatingReadModel()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer("expired-read@example.com"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var issued = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Health,
            Premium = 420m,
            StartDate = today.AddDays(-20),
            EndDate = today.AddDays(-2),
            CoverageDetails = "Expired by date"
        });

        var persisted = await db.Policies.FirstAsync(p => p.Id == issued.Id);
        persisted.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();

        var loaded = await policies.GetByIdAsync(issued.Id);
        loaded.Status.Should().Be(PolicyStatus.Active);
    }

    [Fact]
    public async Task GetAllAsync_FilterExpired_IncludesPastEndDatePoliciesPersistedAsActive()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer("expired-filter@example.com"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var staleActive = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Car,
            Premium = 500m,
            StartDate = today.AddDays(-30),
            EndDate = today.AddDays(-1),
            CoverageDetails = "Old active"
        });
        var active = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Health,
            Premium = 700m,
            StartDate = today.AddDays(1),
            EndDate = today.AddDays(40),
            CoverageDetails = "Current active"
        });

        var stalePersisted = await db.Policies.FirstAsync(p => p.Id == staleActive.Id);
        stalePersisted.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();

        var expiredResults = await policies.GetAllAsync(status: PolicyStatus.Expired, page: 1, pageSize: 20);

        expiredResults.Items.Select(i => i.Id).Should().Contain(staleActive.Id);
        expiredResults.Items.Select(i => i.Id).Should().NotContain(active.Id);
    }

    [Fact]
    public async Task GetAllAsync_SortByStatusDesc_UsesEffectiveReadStatusOrdering()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer("status-sort@example.com"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var staleActive = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Health,
            Premium = 300m,
            StartDate = today.AddDays(-30),
            EndDate = today.AddDays(-1),
            CoverageDetails = "Past due policy"
        });
        var currentActive = await policies.IssueAsync(customer.Id, new Policy
        {
            Type = PolicyType.Life,
            Premium = 800m,
            StartDate = today,
            EndDate = today.AddDays(180),
            CoverageDetails = "Current policy"
        });

        var stalePersisted = await db.Policies.FirstAsync(p => p.Id == staleActive.Id);
        stalePersisted.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();

        var sorted = await policies.GetAllAsync(
            customerId: customer.Id,
            page: 1,
            pageSize: 20,
            sortBy: "status",
            sortDir: "desc");

        sorted.Items.Should().HaveCountGreaterOrEqualTo(2);
        sorted.Items.First().Id.Should().Be(staleActive.Id);
        sorted.Items.Skip(1).Select(i => i.Id).Should().Contain(currentActive.Id);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsNewestFirst()
    {
        await using var db = TestDb.CreateContext();
        var customers = new CustomerService(db);
        var policies = new PolicyService(db);
        var customer = await customers.CreateAsync(NewCustomer("events@example.com"));
        var policy = await policies.IssueAsync(customer.Id, NewPolicy(PolicyType.Health));
        await policies.UpdateAsync(policy.Id, new Policy
        {
            Premium = 600,
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            CoverageDetails = "Updated"
        });

        var events = await policies.GetEventsAsync(policy.Id);

        events.Should().HaveCount(2);
        events.First().EventType.Should().Be(PolicyEventType.Updated);
        events.Last().EventType.Should().Be(PolicyEventType.Issued);
    }

    private static Customer NewCustomer(string email) => new()
    {
        FullName = "Viewer",
        Email = email,
        Phone = "+1-555-0300",
        DateOfBirth = new DateOnly(1991, 2, 2),
        Address = "3 View Road"
    };

    private static Policy NewPolicy(PolicyType type) => new()
    {
        Type = type,
        Premium = 500m,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
        CoverageDetails = "Visibility coverage"
    };
}
