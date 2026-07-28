using InsuranceManagement.Core.Enums;

namespace InsuranceManagement.Core.Entities;

public class PolicyEvent
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public Guid CustomerId { get; set; }
    public PolicyEventType EventType { get; set; }
    public DateTime OccurredAt { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;

    public Policy Policy { get; set; } = null!;
}
