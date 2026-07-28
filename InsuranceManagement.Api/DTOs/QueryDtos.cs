namespace InsuranceManagement.Api.DTOs;

public record CustomerQueryRequest(
    string? Search = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "fullName",
    string SortDir = "asc");

public record PolicyQueryRequest(
    string? Type = null,
    string? Status = null,
    Guid? CustomerId = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "createdAt",
    string SortDir = "desc");
