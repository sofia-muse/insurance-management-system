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
    public DbSet<PolicyEvent> PolicyEvents => Set<PolicyEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

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
            entity.Property(p => p.RowVersion).IsRowVersion();
            entity.HasIndex(p => p.PolicyNumber).IsUnique();
            entity.HasIndex(p => p.CustomerId);
            entity.HasIndex(p => p.Type);
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => new { p.CustomerId, p.Type, p.Status })
                .HasDatabaseName("UX_Policy_OneActiveCarPerCustomer")
                .IsUnique()
                .HasFilter("[Type] = 'Car' AND [Status] = 'Active'");

            entity.HasMany<PolicyEvent>()
                .WithOne(e => e.Policy)
                .HasForeignKey(e => e.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PolicyEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.SnapshotJson).IsRequired().HasMaxLength(4000);
            entity.HasIndex(e => e.PolicyId);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Operation).IsRequired().HasMaxLength(100);
            entity.Property(i => i.Key).IsRequired().HasMaxLength(200);
            entity.Property(i => i.Fingerprint).IsRequired().HasMaxLength(128);
            entity.HasIndex(i => new { i.Operation, i.Key })
                .HasDatabaseName("UX_IdempotencyRecord_Operation_Key")
                .IsUnique();
            entity.HasIndex(i => i.CreatedAt);
        });
    }
}
