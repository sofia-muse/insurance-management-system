namespace InsuranceManagement.Api.Security;

public static class RateLimitPolicies
{
    public const string PerIpBurst = "PerIpBurst";
    public const string WritePerIdentity = "WritePerIdentity";
}
