using InsuranceManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Tests;

internal static class TestDb
{
    public static InsuranceDbContext CreateContext(string? name = null)
    {
        var options = new DbContextOptionsBuilder<InsuranceDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;

        var db = new InsuranceDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
