namespace InsuranceManagement.Api.DTOs;

public record CreateCustomerRequest(
    string FullName,
    string Email,
    string Phone,
    DateOnly DateOfBirth,
    string Address);

public record UpdateCustomerRequest(
    string FullName,
    string Email,
    string Phone,
    DateOnly DateOfBirth,
    string Address);

public record CustomerResponse(
    Guid Id,
    string FullName,
    string Email,
    string Phone,
    DateOnly DateOfBirth,
    string Address,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<PolicySummaryResponse>? Policies = null);

public record PolicySummaryResponse(
    Guid Id,
    string PolicyNumber,
    string Type,
    string Status,
    decimal Premium);
