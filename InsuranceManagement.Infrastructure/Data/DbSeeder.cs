using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace InsuranceManagement.Infrastructure.Data;

public static class DbSeeder
{
    private const string BaselineMigrationId = "20260728080634_ProductionPolishBaseline";
    private const string ConcurrencyMigrationId = "20260728082236_ConcurrencyAndActiveCarInvariant";

    public static async Task SeedAsync(
        IServiceProvider services,
        bool runMigrationsOnStartup = true,
        bool runSeedOnStartup = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
        if (runMigrationsOnStartup && db.Database.IsRelational())
        {
            await EnsureMigrationHistoryForLegacyDatabaseAsync(db);
            await db.Database.MigrateAsync();
        }

        if (!runMigrationsOnStartup && runSeedOnStartup && db.Database.IsRelational())
        {
            await EnsureSchemaReadyForSeedingAsync(db);
        }

        if (!runSeedOnStartup)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var customers = new[]
        {
            NewCustomer("11111111-1111-1111-1111-111111111111", "Alice Johnson", "alice.johnson@example.com", "+1-555-0101", new DateOnly(1988, 4, 12), "12 Maple Street, Springfield", now),
            NewCustomer("22222222-2222-2222-2222-222222222222", "Bob Smith", "bob.smith@example.com", "+1-555-0102", new DateOnly(1975, 9, 3), "88 Oak Avenue, Rivertown", now),
            NewCustomer("44444444-4444-4444-4444-444444444444", "Carla Green", "carla.green@example.com", "+1-555-0103", new DateOnly(1992, 6, 23), "7 Pine Road, Lakeview", now),
            NewCustomer("55555555-5555-5555-5555-555555555555", "David Brown", "david.brown@example.com", "+1-555-0104", new DateOnly(1980, 12, 1), "54 Cedar Ave, Northton", now),
            NewCustomer("66666666-6666-6666-6666-666666666666", "Emma White", "emma.white@example.com", "+1-555-0105", new DateOnly(1995, 2, 17), "99 Birch St, Hilltown", now),
            NewCustomer("77777777-7777-7777-7777-777777777777", "Frank Lee", "frank.lee@example.com", "+1-555-0106", new DateOnly(1972, 8, 9), "24 River Dr, Southbay", now),
            NewCustomer("88888888-8888-8888-8888-888888888888", "Grace Kim", "grace.kim@example.com", "+1-555-0107", new DateOnly(1987, 11, 30), "16 Garden Ln, Brookfield", now),
            NewCustomer("99999999-9999-9999-9999-999999999999", "Henry Clark", "henry.clark@example.com", "+1-555-0108", new DateOnly(1990, 5, 5), "402 Elm Way, Riverpoint", now),
            NewCustomer("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Irene Adams", "irene.adams@example.com", "+1-555-0109", new DateOnly(1983, 1, 14), "71 Palm Ct, Oldtown", now),
            NewCustomer("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Jack Wilson", "jack.wilson@example.com", "+1-555-0110", new DateOnly(1979, 7, 27), "3 Harbor St, Midtown", now)
        };

        var existingCustomers = await db.Customers
            .Select(c => new { c.Id, c.Email })
            .ToListAsync();
        var existingCustomerIds = existingCustomers.Select(c => c.Id).ToHashSet();
        var existingEmails = existingCustomers.Select(c => c.Email.ToLowerInvariant()).ToHashSet();
        var customersToAdd = customers.Where(c =>
            !existingCustomerIds.Contains(c.Id) &&
            !existingEmails.Contains(c.Email.ToLowerInvariant()))
            .ToList();
        db.Customers.AddRange(customersToAdd);
        var seedableCustomerIds = existingCustomerIds
            .Concat(customersToAdd.Select(c => c.Id))
            .ToHashSet();

        var policies = new[]
        {
            NewPolicy("33333333-3333-3333-3333-333333333333", customers[0].Id, "POL-CAR-SEED001", PolicyType.Car, PolicyStatus.Active, 1200m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Comprehensive auto coverage including collision and liability.", now),
            NewPolicy("cccccccc-cccc-cccc-cccc-cccccccccccc", customers[0].Id, "POL-HEALTH-SEED001", PolicyType.Health, PolicyStatus.Active, 780m, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Standard health plan with outpatient coverage.", now),
            NewPolicy("dddddddd-dddd-dddd-dddd-dddddddddddd", customers[1].Id, "POL-LIFE-SEED001", PolicyType.Life, PolicyStatus.Cancelled, 430m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), "20-year term life policy.", now, now),
            NewPolicy("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", customers[2].Id, "POL-CAR-SEED002", PolicyType.Car, PolicyStatus.Expired, 910m, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), "Liability-only car coverage.", now),
            NewPolicy("ffffffff-ffff-ffff-ffff-ffffffffffff", customers[3].Id, "POL-HEALTH-SEED002", PolicyType.Health, PolicyStatus.Active, 995m, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), "Family health package with dental.", now),
            NewPolicy("12121212-1212-1212-1212-121212121212", customers[4].Id, "POL-LIFE-SEED002", PolicyType.Life, PolicyStatus.Active, 510m, new DateOnly(2026, 1, 1), new DateOnly(2036, 12, 31), "Long-term life coverage.", now),
            NewPolicy("13131313-1313-1313-1313-131313131313", customers[5].Id, "POL-CAR-SEED003", PolicyType.Car, PolicyStatus.Cancelled, 1110m, new DateOnly(2025, 6, 1), new DateOnly(2026, 5, 31), "Premium car plan with roadside assistance.", now, now),
            NewPolicy("14141414-1414-1414-1414-141414141414", customers[6].Id, "POL-HEALTH-SEED003", PolicyType.Health, PolicyStatus.Expired, 640m, new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31), "Basic health policy.", now),
            NewPolicy("15151515-1515-1515-1515-151515151515", customers[7].Id, "POL-LIFE-SEED003", PolicyType.Life, PolicyStatus.Active, 720m, new DateOnly(2026, 4, 1), new DateOnly(2031, 3, 31), "5-year renewable life plan.", now),
            NewPolicy("16161616-1616-1616-1616-161616161616", customers[8].Id, "POL-HEALTH-SEED004", PolicyType.Health, PolicyStatus.Active, 875m, new DateOnly(2026, 2, 1), new DateOnly(2027, 1, 31), "Enhanced health plan.", now),
            NewPolicy("17171717-1717-1717-1717-171717171717", customers[9].Id, "POL-CAR-SEED004", PolicyType.Car, PolicyStatus.Active, 1320m, new DateOnly(2026, 5, 1), new DateOnly(2027, 4, 30), "Premium car coverage with zero deductible.", now),
            NewPolicy("18181818-1818-1818-1818-181818181818", customers[9].Id, "POL-LIFE-SEED004", PolicyType.Life, PolicyStatus.Active, 600m, new DateOnly(2026, 5, 1), new DateOnly(2046, 4, 30), "20-year term coverage.", now)
        };
        var existingPolicies = await db.Policies
            .Select(p => new { p.Id, p.PolicyNumber })
            .ToListAsync();
        var existingPolicyIds = existingPolicies.Select(p => p.Id).ToHashSet();
        var existingPolicyNumbers = existingPolicies.Select(p => p.PolicyNumber).ToHashSet();
        var policiesToAdd = policies.Where(p =>
            seedableCustomerIds.Contains(p.CustomerId) &&
            !existingPolicyIds.Contains(p.Id) &&
            !existingPolicyNumbers.Contains(p.PolicyNumber))
            .ToList();
        db.Policies.AddRange(policiesToAdd);
        var seedablePolicyIds = existingPolicyIds
            .Concat(policiesToAdd.Select(p => p.Id))
            .ToHashSet();

        var policyEvents = new[]
        {
            NewPolicyEvent("21111111-1111-1111-1111-111111111111", Guid.Parse("33333333-3333-3333-3333-333333333333"), customers[0].Id, PolicyEventType.Issued, now, "Issued seed car policy."),
            NewPolicyEvent("22222222-1111-1111-1111-111111111111", Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), customers[1].Id, PolicyEventType.Issued, now.AddDays(-30), "Issued seed life policy."),
            NewPolicyEvent("23333333-1111-1111-1111-111111111111", Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), customers[1].Id, PolicyEventType.Cancelled, now.AddDays(-1), "Cancelled seed life policy."),
            NewPolicyEvent("24444444-1111-1111-1111-111111111111", Guid.Parse("13131313-1313-1313-1313-131313131313"), customers[5].Id, PolicyEventType.Issued, now.AddDays(-20), "Issued seed car policy."),
            NewPolicyEvent("25555555-1111-1111-1111-111111111111", Guid.Parse("13131313-1313-1313-1313-131313131313"), customers[5].Id, PolicyEventType.Cancelled, now.AddDays(-5), "Cancelled seed car policy.")
        };
        var existingEventIds = (await db.PolicyEvents
            .Select(e => e.Id)
            .ToListAsync())
            .ToHashSet();
        db.PolicyEvents.AddRange(policyEvents.Where(e =>
            seedablePolicyIds.Contains(e.PolicyId) &&
            seedableCustomerIds.Contains(e.CustomerId) &&
            !existingEventIds.Contains(e.Id)));

        await db.SaveChangesAsync();
    }

    private static async Task EnsureMigrationHistoryForLegacyDatabaseAsync(InsuranceDbContext db)
    {
        var historyExists = await TableExistsAsync(db, "__EFMigrationsHistory");

        // If history table exists, normal migration flow is safe.
        if (historyExists)
        {
            return;
        }

        var hasAllBaselineTables =
            await TableExistsAsync(db, "Customers") &&
            await TableExistsAsync(db, "Policies") &&
            await TableExistsAsync(db, "PolicyEvents") &&
            await TableExistsAsync(db, "IdempotencyRecords");

        var hasAnyBaselineTable =
            await TableExistsAsync(db, "Customers")
            || await TableExistsAsync(db, "Policies")
            || await TableExistsAsync(db, "PolicyEvents")
            || await TableExistsAsync(db, "IdempotencyRecords");

        if (hasAnyBaselineTable && !hasAllBaselineTables)
        {
            throw new InvalidOperationException(
                "Legacy database contains a partial baseline schema without migration history. "
                + "Complete or clean baseline tables before running migrations.");
        }

        // Only stamp migration history if the schema already matches the baseline shape.
        if (!hasAllBaselineTables)
        {
            return;
        }

        await EnsureLegacySchemaCompatibleWithBaselineAsync(db);

        // Legacy DB created before migrations: create and backfill history table to baseline.
        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory')
            BEGIN
                CREATE TABLE [__EFMigrationsHistory](
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END
            """);

        var efProductVersion = db.Model.GetProductVersion() ?? "8.0.0";
        await StampMigrationAsync(db, BaselineMigrationId, efProductVersion);

        var hasConcurrencyArtifacts =
            await ColumnExistsAsync(db, "Policies", "RowVersion")
            && await IndexExistsAsync(db, "Policies", "UX_Policy_OneActiveCarPerCustomer");

        if (hasConcurrencyArtifacts)
        {
            await StampMigrationAsync(db, ConcurrencyMigrationId, efProductVersion);
        }
    }

    private static async Task EnsureSchemaReadyForSeedingAsync(InsuranceDbContext db)
    {
        var hasRequiredTables =
            await TableExistsAsync(db, "Customers")
            && await TableExistsAsync(db, "Policies")
            && await TableExistsAsync(db, "PolicyEvents")
            && await TableExistsAsync(db, "IdempotencyRecords");

        if (!hasRequiredTables)
        {
            throw new InvalidOperationException(
                "Database:RunSeedOnStartup requires an existing schema when migrations are disabled. "
                + "Enable migrations on startup or provision the schema before seeding.");
        }
    }

    private static async Task StampMigrationAsync(InsuranceDbContext db, string migrationId, string productVersion)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
              IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = {migrationId})
              BEGIN
                  INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                  VALUES ({migrationId}, {productVersion});
              END
              """);
    }

    private static async Task EnsureLegacySchemaCompatibleWithBaselineAsync(InsuranceDbContext db)
    {
        var missingArtifacts = new List<string>();
        if (!await ColumnExistsAsync(db, "Customers", "Email"))
        {
            missingArtifacts.Add("Customers.Email");
        }

        if (!await ColumnExistsAsync(db, "Policies", "PolicyNumber"))
        {
            missingArtifacts.Add("Policies.PolicyNumber");
        }

        if (!await ColumnExistsAsync(db, "PolicyEvents", "SnapshotJson"))
        {
            missingArtifacts.Add("PolicyEvents.SnapshotJson");
        }

        if (!await IndexExistsAsync(db, "IdempotencyRecords", "UX_IdempotencyRecord_Operation_Key"))
        {
            missingArtifacts.Add("IdempotencyRecords.UX_IdempotencyRecord_Operation_Key");
        }

        if (missingArtifacts.Count > 0)
        {
            throw new InvalidOperationException(
                "Legacy database schema is incompatible with baseline migration; missing required artifacts: "
                + string.Join(", ", missingArtifacts));
        }
    }

    private static async Task<bool> TableExistsAsync(InsuranceDbContext db, string tableName)
    {
        return await ExistsByCountAsync(
            db,
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = @tableName
            """,
            ("@tableName", tableName));
    }

    private static async Task<bool> ColumnExistsAsync(InsuranceDbContext db, string tableName, string columnName)
    {
        return await ExistsByCountAsync(
            db,
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName
            """,
            ("@tableName", tableName),
            ("@columnName", columnName));
    }

    private static async Task<bool> IndexExistsAsync(InsuranceDbContext db, string tableName, string indexName)
    {
        return await ExistsByCountAsync(
            db,
            """
            SELECT COUNT(*)
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name = @tableName AND i.name = @indexName
            """,
            ("@tableName", tableName),
            ("@indexName", indexName));
    }

    private static async Task<bool> ExistsByCountAsync(
        InsuranceDbContext db,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static Customer NewCustomer(
        string id,
        string fullName,
        string email,
        string phone,
        DateOnly dateOfBirth,
        string address,
        DateTime now) =>
        new()
        {
            Id = Guid.Parse(id),
            FullName = fullName,
            Email = email,
            Phone = phone,
            DateOfBirth = dateOfBirth,
            Address = address,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Policy NewPolicy(
        string id,
        Guid customerId,
        string policyNumber,
        PolicyType type,
        PolicyStatus status,
        decimal premium,
        DateOnly startDate,
        DateOnly endDate,
        string coverageDetails,
        DateTime now,
        DateTime? cancelledAt = null) =>
        new()
        {
            Id = Guid.Parse(id),
            CustomerId = customerId,
            PolicyNumber = policyNumber,
            Type = type,
            Status = status,
            Premium = premium,
            StartDate = startDate,
            EndDate = endDate,
            CoverageDetails = coverageDetails,
            CreatedAt = now,
            UpdatedAt = now,
            CancelledAt = cancelledAt
        };

    private static PolicyEvent NewPolicyEvent(
        string id,
        Guid policyId,
        Guid customerId,
        PolicyEventType eventType,
        DateTime occurredAt,
        string note) =>
        new()
        {
            Id = Guid.Parse(id),
            PolicyId = policyId,
            CustomerId = customerId,
            EventType = eventType,
            OccurredAt = occurredAt,
            SnapshotJson = $$"""
                             {"note":"{{note}}","policyId":"{{policyId}}","eventType":"{{eventType}}"}
                             """
        };
}
