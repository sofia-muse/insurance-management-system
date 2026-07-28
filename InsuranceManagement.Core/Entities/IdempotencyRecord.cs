namespace InsuranceManagement.Core.Entities;

public class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public Guid ResponseEntityId { get; set; }
    public DateTime CreatedAt { get; set; }
}
