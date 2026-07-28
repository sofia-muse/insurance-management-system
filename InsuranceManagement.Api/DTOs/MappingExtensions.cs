using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Models;
using InsuranceManagement.Core.Utilities;
using System.Text.Json;

namespace InsuranceManagement.Api.DTOs;

public static class MappingExtensions
{
    public static Customer ToEntity(this CreateCustomerRequest request)
        => MapCustomer(request.FullName, request.Email, request.Phone, request.DateOfBirth, request.Address);

    public static Customer ToEntity(this UpdateCustomerRequest request)
        => MapCustomer(request.FullName, request.Email, request.Phone, request.DateOfBirth, request.Address);

    private static Customer MapCustomer(string fullName, string email, string phone, DateOnly dateOfBirth, string address)
    {
        return new Customer
        {
            FullName = fullName.Trim(),
            Email = email.Trim(),
            Phone = phone.Trim(),
            DateOfBirth = dateOfBirth,
            Address = address.Trim()
        };
    }

    public static Policy ToEntity(this IssuePolicyRequest request)
    {
        return new Policy
        {
            Type = request.Type,
            Premium = request.Premium,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CoverageDetails = request.CoverageDetails.Trim()
        };
    }

    public static Policy ToEntity(this UpdatePolicyRequest request)
    {
        return new Policy
        {
            Premium = request.Premium,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CoverageDetails = request.CoverageDetails.Trim()
        };
    }

    public static CustomerResponse ToResponse(this Customer customer, bool includePolicies = false)
    {
        return new CustomerResponse(
            customer.Id,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.DateOfBirth,
            customer.Address,
            customer.CreatedAt,
            customer.UpdatedAt,
            includePolicies
                ? customer.Policies
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => new PolicySummaryResponse(
                        p.Id,
                        p.PolicyNumber,
                        p.Type.ToString(),
                        PolicyStatusResolver.ResolveForRead(p.Status, p.EndDate).ToString(),
                        p.Premium))
                    .ToList()
                : null);
    }

    public static PolicyResponse ToResponse(this Policy policy)
    {
        return new PolicyResponse(
            policy.Id,
            policy.CustomerId,
            policy.PolicyNumber,
            policy.Type.ToString(),
            PolicyStatusResolver.ResolveForRead(policy.Status, policy.EndDate).ToString(),
            policy.Premium,
            policy.StartDate,
            policy.EndDate,
            policy.CoverageDetails,
            policy.CreatedAt,
            policy.UpdatedAt,
            policy.CancelledAt);
    }

    public static PolicyEventResponse ToResponse(this PolicyEvent evt)
    {
        string? note = null;
        try
        {
            using var document = JsonDocument.Parse(evt.SnapshotJson);
            if (document.RootElement.TryGetProperty("note", out var noteElement))
            {
                note = noteElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed legacy snapshots and keep note null.
        }

        return new PolicyEventResponse(
            evt.Id,
            evt.PolicyId,
            evt.CustomerId,
            evt.EventType.ToString(),
            evt.OccurredAt,
            note);
    }

    public static PagedResponse<TOut> ToResponse<TIn, TOut>(
        this PagedResult<TIn> pagedResult,
        Func<TIn, TOut> mapper)
    {
        return new PagedResponse<TOut>(
            pagedResult.Items.Select(mapper).ToList(),
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalItems,
            pagedResult.TotalPages);
    }

}
