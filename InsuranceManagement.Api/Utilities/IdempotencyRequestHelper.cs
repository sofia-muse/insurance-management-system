using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsuranceManagement.Api.DTOs;

namespace InsuranceManagement.Api.Utilities;

internal static class IdempotencyRequestHelper
{
    private const int MaxIdempotencyKeyLength = 200;
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9\\-_.]+$", RegexOptions.Compiled);

    public static string BuildFingerprint(object payload)
    {
        // SHA-256 hex output provides deterministic, compact replay checks.
        var json = JsonSerializer.Serialize(payload);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public static string? NormalizeKey(string? idempotencyKey)
        => string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

    public static ErrorResponse? ValidateKey(string? idempotencyKey, string traceId)
    {
        // Idempotency key is optional; when provided it must stay constrained
        // so it is safe for storage, logging, and transport across systems.
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var normalized = idempotencyKey.Trim();
        if (normalized.Length <= MaxIdempotencyKeyLength && IdempotencyKeyPattern.IsMatch(normalized))
        {
            return null;
        }

        return new ErrorResponse(
            "Validation Failed",
            StatusCodes.Status400BadRequest,
            "INVALID_IDEMPOTENCY_KEY",
            "Idempotency-Key must be <= 200 characters and contain only letters, digits, '-', '_' or '.'.",
            traceId);
    }
}
