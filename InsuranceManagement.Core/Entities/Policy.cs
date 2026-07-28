using InsuranceManagement.Core.Enums;

namespace InsuranceManagement.Core.Entities;

public class Policy
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string PolicyNumber { get; set; } = string.Empty;
    public PolicyType Type { get; set; }
    public PolicyStatus Status { get; set; }
    public decimal Premium { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string CoverageDetails { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public Customer Customer { get; set; } = null!;
}
