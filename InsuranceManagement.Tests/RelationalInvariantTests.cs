using FluentAssertions;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace InsuranceManagement.Tests;

public class RelationalInvariantTests
{
    [Fact]
    public void Model_ContainsFilteredUniqueIndex_ForActiveCarInvariant()
    {
        using var db = TestDb.CreateContext();
        var policyType = db.Model.FindEntityType(typeof(Policy));
        policyType.Should().NotBeNull();

        var index = policyType!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "UX_Policy_OneActiveCarPerCustomer");

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("[Type] = 'Car' AND [Status] = 'Active'");
    }

    [Fact]
    public void Model_UsesConcurrencyToken_ForPolicyRowVersion()
    {
        using var db = TestDb.CreateContext();
        var policyType = db.Model.FindEntityType(typeof(Policy));
        var rowVersionProperty = policyType?.FindProperty(nameof(Policy.RowVersion));

        rowVersionProperty.Should().NotBeNull();
        rowVersionProperty!.IsConcurrencyToken.Should().BeTrue();
        rowVersionProperty.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
    }

    [Fact]
    public async Task LocalDb_EnforcesSingleActiveCarPolicyPerCustomer()
    {
        var databaseName = $"insurance-relational-tests-{Guid.NewGuid():N}";
        var connectionString = BuildLocalDbConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<InsuranceDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new InsuranceDbContext(options);
        await EnsureLocalDbReadyAsync(db);

        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            FullName = "SQLite User",
            Email = "sqlite-user@example.com",
            Phone = "+1-555-3030",
            DateOfBirth = new DateOnly(1980, 1, 1),
            Address = "SQLite Street",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.Policies.Add(new Policy
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PolicyNumber = "POL-CAR-SQLITE-001",
            Type = PolicyType.Car,
            Status = PolicyStatus.Active,
            Premium = 1000m,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CoverageDetails = "First active car policy",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.Policies.Add(new Policy
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PolicyNumber = "POL-CAR-SQLITE-002",
            Type = PolicyType.Car,
            Status = PolicyStatus.Active,
            Premium = 1200m,
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CoverageDetails = "Second active car policy",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task LocalDb_RowVersionDetectsConcurrentUpdates()
    {
        var databaseName = $"insurance-concurrency-tests-{Guid.NewGuid():N}";
        var connectionString = BuildLocalDbConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<InsuranceDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var setupContext = new InsuranceDbContext(options);
        await EnsureLocalDbReadyAsync(setupContext);

        var customerId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        setupContext.Customers.Add(new Customer
        {
            Id = customerId,
            FullName = "Concurrency Customer",
            Email = $"concurrency-{Guid.NewGuid():N}@example.com",
            Phone = "+1-555-4040",
            DateOfBirth = new DateOnly(1985, 1, 1),
            Address = "Concurrency Road",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        setupContext.Policies.Add(new Policy
        {
            Id = policyId,
            CustomerId = customerId,
            PolicyNumber = $"POL-LIFE-{Guid.NewGuid():N}"[..18],
            Type = PolicyType.Life,
            Status = PolicyStatus.Active,
            Premium = 500m,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CoverageDetails = "Original",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await setupContext.SaveChangesAsync();

        await using var contextA = new InsuranceDbContext(options);
        await using var contextB = new InsuranceDbContext(options);

        var policyA = await contextA.Policies.SingleAsync(p => p.Id == policyId);
        var policyB = await contextB.Policies.SingleAsync(p => p.Id == policyId);

        policyA.CoverageDetails = "Update A";
        policyA.UpdatedAt = DateTime.UtcNow;
        await contextA.SaveChangesAsync();

        policyB.CoverageDetails = "Update B";
        policyB.UpdatedAt = DateTime.UtcNow;
        var act = async () => await contextB.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await setupContext.Database.EnsureDeletedAsync();
    }

    private static string BuildLocalDbConnectionString(string databaseName)
    {
        return $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
    }

    private static async Task EnsureLocalDbReadyAsync(InsuranceDbContext db)
    {
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "LocalDB is required for relational invariant tests. Ensure SQL Server LocalDB is installed and running.",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Failed to initialize relational test database. Check LocalDB availability and migrations.",
                ex);
        }
    }
}
