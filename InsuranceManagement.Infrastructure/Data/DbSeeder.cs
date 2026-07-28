using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InsuranceManagement.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (await db.Customers.AnyAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var alice = new Customer
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FullName = "Alice Johnson",
            Email = "alice.johnson@example.com",
            Phone = "+1-555-0101",
            DateOfBirth = new DateOnly(1988, 4, 12),
            Address = "12 Maple Street, Springfield",
            CreatedAt = now,
            UpdatedAt = now
        };

        var bob = new Customer
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            FullName = "Bob Smith",
            Email = "bob.smith@example.com",
            Phone = "+1-555-0102",
            DateOfBirth = new DateOnly(1975, 9, 3),
            Address = "88 Oak Avenue, Rivertown",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Customers.AddRange(alice, bob);

        db.Policies.Add(new Policy
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CustomerId = alice.Id,
            PolicyNumber = "POL-CAR-SEED001",
            Type = PolicyType.Car,
            Status = PolicyStatus.Active,
            Premium = 1200.00m,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CoverageDetails = "Comprehensive auto coverage including collision and liability.",
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }
}
