using InsuranceManagement.Api.DTOs;
using InsuranceManagement.Api.Security;
using InsuranceManagement.Api.Utilities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InsuranceManagement.Api.Controllers;

[ApiController]
[Route("api")]
[EnableRateLimiting(RateLimitPolicies.PerIpBurst)]
// Thin HTTP layer for policy operations:
// validates request-shape concerns, builds idempotency fingerprints,
// and delegates business rules to PolicyService.
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policyService;

    public PoliciesController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    [HttpPost("customers/{customerId:guid}/policies")]
    [Authorize(Policy = AuthPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitPolicies.WritePerIdentity)]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PolicyResponse>> Issue(
        Guid customerId,
        [FromBody] IssuePolicyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotencyValidationResult = TryGetValidatedIdempotencyKey(idempotencyKey);
        if (idempotencyValidationResult.ErrorResult is not null)
        {
            return idempotencyValidationResult.ErrorResult;
        }

        var payloadFingerprint = string.IsNullOrWhiteSpace(idempotencyValidationResult.Key)
            ? null
            : IdempotencyRequestHelper.BuildFingerprint(new
            {
                customerId,
                request.Type,
                request.Premium,
                request.StartDate,
                request.EndDate,
                CoverageDetails = request.CoverageDetails.Trim()
            });

        var policy = await _policyService.IssueAsync(
            customerId,
            request.ToEntity(),
            idempotencyValidationResult.Key,
            payloadFingerprint,
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, policy.ToResponse());
    }

    [HttpGet("policies")]
    [ProducesResponseType(typeof(PagedResponse<PolicyResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PolicyResponse>>> GetAll(
        [FromQuery] PolicyQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        PolicyType? type = Enum.TryParse<PolicyType>(query.Type, true, out var parsedType) ? parsedType : null;
        PolicyStatus? status = Enum.TryParse<PolicyStatus>(query.Status, true, out var parsedStatus) ? parsedStatus : null;
        var policies = await _policyService.GetAllAsync(type, status, query.CustomerId, query.Page, query.PageSize, query.SortBy, query.SortDir, cancellationToken);
        return Ok(policies.ToResponse(p => p.ToResponse()));
    }

    [HttpGet("policies/{id:guid}")]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var policy = await _policyService.GetByIdAsync(id, cancellationToken);
        return Ok(policy.ToResponse());
    }

    [HttpGet("policies/{id:guid}/events")]
    [ProducesResponseType(typeof(IEnumerable<PolicyEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<PolicyEventResponse>>> GetEvents(Guid id, CancellationToken cancellationToken)
    {
        var events = await _policyService.GetEventsAsync(id, cancellationToken);
        return Ok(events.Select(e => e.ToResponse()));
    }

    [HttpPut("policies/{id:guid}")]
    [Authorize(Policy = AuthPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitPolicies.WritePerIdentity)]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyResponse>> Update(Guid id, [FromBody] UpdatePolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _policyService.UpdateAsync(id, request.ToEntity(), cancellationToken);

        return Ok(policy.ToResponse());
    }

    [HttpPost("policies/{id:guid}/cancel")]
    [Authorize(Policy = AuthPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitPolicies.WritePerIdentity)]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PolicyResponse>> Cancel(
        Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotencyValidationResult = TryGetValidatedIdempotencyKey(idempotencyKey);
        if (idempotencyValidationResult.ErrorResult is not null)
        {
            return idempotencyValidationResult.ErrorResult;
        }

        var payloadFingerprint = string.IsNullOrWhiteSpace(idempotencyValidationResult.Key)
            ? null
            : IdempotencyRequestHelper.BuildFingerprint(new { id, operation = "cancel" });
        var policy = await _policyService.CancelAsync(id, idempotencyValidationResult.Key, payloadFingerprint, cancellationToken);
        return Ok(policy.ToResponse());
    }

    private (string? Key, ActionResult<PolicyResponse>? ErrorResult) TryGetValidatedIdempotencyKey(string? rawKey)
    {
        var normalizedIdempotencyKey = IdempotencyRequestHelper.NormalizeKey(rawKey);
        var validationError = IdempotencyRequestHelper.ValidateKey(normalizedIdempotencyKey, HttpContext.TraceIdentifier);
        return validationError is null
            ? (normalizedIdempotencyKey, null)
            : (null, BadRequest(validationError));
    }
}
