using InsuranceManagement.Api.DTOs;

namespace InsuranceManagement.Api.Utilities;

internal static class ApiErrorResponseWriter
{
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string code,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = new ErrorResponse(
            title,
            statusCode,
            code,
            detail,
            context.TraceIdentifier);

        return context.Response.WriteAsJsonAsync(payload);
    }
}
