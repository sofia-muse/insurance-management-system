using InsuranceManagement.Core.Enums;

namespace InsuranceManagement.Core.Utilities;

public static class PolicyStatusResolver
{
    public static PolicyStatus ResolveForRead(PolicyStatus persistedStatus, DateOnly endDate, DateOnly? today = null)
    {
        if (persistedStatus == PolicyStatus.Cancelled)
        {
            return persistedStatus;
        }

        var currentDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (endDate < currentDate)
        {
            return PolicyStatus.Expired;
        }

        return persistedStatus;
    }
}
