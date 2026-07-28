using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace InsuranceManagement.Infrastructure.Data;

public class InsuranceDbContext : DbContext
{
    public InsuranceDbContext(DbContextOptions<InsuranceDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Policy> Policies => Set<Policy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.FullName).IsRequired().HasMaxLength(200);
            entity.Property(c => c.Email).IsRequired().HasMaxLength(256);
            entity.Property(c => c.Phone).IsRequired().HasMaxLength(50);
            entity.Property(c => c.Address).IsRequired().HasMaxLength(500);
            entity.HasIndex(c => c.Email).IsUnique();

            entity.HasMany(c => c.Policies)
                .WithOne(p => p.Customer)
                .HasForeignKey(p => p.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Policy>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.PolicyNumber).IsRequired().HasMaxLength(50);
            entity.Property(p => p.Premium).HasPrecision(18, 2);
            entity.Property(p => p.CoverageDetails).IsRequired().HasMaxLength(2000);
            entity.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(p => p.PolicyNumber).IsUnique();
            entity.HasIndex(p => p.CustomerId);
            entity.HasIndex(p => p.Type);
            entity.HasIndex(p => p.Status);
        });
    }
}
