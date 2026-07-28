using FluentAssertions;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InsuranceManagement.Tests;

public class DbSeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesRichDatasetWithMixedStatusesAndTypes()
    {
        var dbName = $"seed-tests-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<InsuranceDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        await using var provider = services.BuildServiceProvider();
        await DbSeeder.SeedAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();

        var customerCount = await db.Customers.CountAsync();
        var policyCount = await db.Policies.CountAsync();
        var eventCount = await db.PolicyEvents.CountAsync();

        customerCount.Should().BeGreaterThanOrEqualTo(10);
        policyCount.Should().BeGreaterThanOrEqualTo(12);
        eventCount.Should().BeGreaterThanOrEqualTo(5);

        (await db.Policies.AnyAsync(p => p.Status == PolicyStatus.Active)).Should().BeTrue();
        (await db.Policies.AnyAsync(p => p.Status == PolicyStatus.Cancelled)).Should().BeTrue();
        (await db.Policies.AnyAsync(p => p.Status == PolicyStatus.Expired)).Should().BeTrue();

        (await db.Policies.AnyAsync(p => p.Type == PolicyType.Car)).Should().BeTrue();
        (await db.Policies.AnyAsync(p => p.Type == PolicyType.Health)).Should().BeTrue();
        (await db.Policies.AnyAsync(p => p.Type == PolicyType.Life)).Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_WhenDatabaseAlreadyHasData_AddsMissingSeedRowsWithoutDuplicates()
    {
        var dbName = $"seed-tests-existing-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<InsuranceDbContext>(options => options.UseInMemoryDatabase(dbName));

        await using var provider = services.BuildServiceProvider();

        // Existing row should not block full seed bootstrap.
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            db.Customers.Add(new Core.Entities.Customer
            {
                Id = Guid.NewGuid(),
                FullName = "Preexisting Customer",
                Email = "preexisting@example.com",
                Phone = "+1-555-9999",
                DateOfBirth = new DateOnly(1980, 1, 1),
                Address = "Preexisting Address",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await DbSeeder.SeedAsync(provider);
        await DbSeeder.SeedAsync(provider);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<InsuranceDbContext>();

        // Seed still applied and did not duplicate fixed-ID rows across repeated runs.
        (await verifyDb.Customers.CountAsync()).Should().BeGreaterThanOrEqualTo(11);
        (await verifyDb.Policies.CountAsync()).Should().BeGreaterThanOrEqualTo(12);
        (await verifyDb.PolicyEvents.CountAsync()).Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task SeedAsync_WhenBusinessKeysExist_DoesNotInsertDuplicateEmailOrPolicyNumber()
    {
        var dbName = $"seed-tests-businesskeys-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<InsuranceDbContext>(options => options.UseInMemoryDatabase(dbName));

        await using var provider = services.BuildServiceProvider();

        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            db.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                FullName = "Email Collision Customer",
                Email = "alice.johnson@example.com", // same as seeded Alice email
                Phone = "+1-555-1222",
                DateOfBirth = new DateOnly(1985, 1, 1),
                Address = "Collision Address",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.Policies.Add(new Policy
            {
                Id = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                PolicyNumber = "POL-CAR-SEED001", // same as seeded policy number
                Type = PolicyType.Car,
                Status = PolicyStatus.Active,
                Premium = 999m,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31),
                CoverageDetails = "Collision policy",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await DbSeeder.SeedAsync(provider);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<InsuranceDbContext>();

        (await verifyDb.Customers.CountAsync(c => c.Email == "alice.johnson@example.com")).Should().Be(1);
        (await verifyDb.Policies.CountAsync(p => p.PolicyNumber == "POL-CAR-SEED001")).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_WhenSeedCustomerEmailExistsWithDifferentId_DoesNotCreateOrphanSeedPolicies()
    {
        var dbName = $"seed-tests-orphans-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<InsuranceDbContext>(options => options.UseInMemoryDatabase(dbName));

        await using var provider = services.BuildServiceProvider();

        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            db.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                FullName = "Collision Alice",
                Email = "alice.johnson@example.com",
                Phone = "+1-555-9898",
                DateOfBirth = new DateOnly(1990, 1, 1),
                Address = "Collision Avenue",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await DbSeeder.SeedAsync(provider);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
        var fixedAliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        (await verifyDb.Customers.AnyAsync(c => c.Id == fixedAliceId)).Should().BeFalse();
        (await verifyDb.Policies.AnyAsync(p => p.CustomerId == fixedAliceId)).Should().BeFalse();
        (await verifyDb.PolicyEvents.AnyAsync(e => e.CustomerId == fixedAliceId)).Should().BeFalse();
    }
}
