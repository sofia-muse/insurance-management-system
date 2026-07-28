using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using InsuranceManagement.Core.Models;

namespace InsuranceManagement.Infrastructure.Services;

internal static class ServiceQueryHelpers
{
    public static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    public static bool IsDescending(string? sortDir)
        => string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

    public static bool IsUniqueConstraintViolation(DbUpdateException ex, string constraintName)
    {
        if (ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627)
        {
            return sqlEx.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
        }

        return ex.InnerException?.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase) == true
            || ex.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        Action<List<T>>? postProcess = null)
    {
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        postProcess?.Invoke(items);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<T>(items, page, pageSize, totalItems, totalPages);
    }
}
