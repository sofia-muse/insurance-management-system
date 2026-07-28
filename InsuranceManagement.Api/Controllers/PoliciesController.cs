using InsuranceManagement.Api.DTOs;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace InsuranceManagement.Api.Controllers;

[ApiController]
[Route("api")]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policyService;

    public PoliciesController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    /// <summary>Issue a new policy and link it to an existing customer.</summary>
    [HttpPost("customers/{customerId:guid}/policies")]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PolicyResponse>> Issue(
        Guid customerId,
        [FromBody] IssuePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _policyService.IssueAsync(customerId, new Policy
        {
            Type = request.Type,
            Premium = request.Premium,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CoverageDetails = request.CoverageDetails.Trim()
        }, cancellationToken);

        var response = policy.ToResponse();
        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, response);
    }

    /// <summary>Retrieve and filter policies by type, status, and/or customer.</summary>
    [HttpGet("policies")]
    [ProducesResponseType(typeof(IEnumerable<PolicyResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PolicyResponse>>> GetAll(
        [FromQuery] PolicyType? type,
        [FromQuery] PolicyStatus? status,
        [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        var policies = await _policyService.GetAllAsync(type, status, customerId, cancellationToken);
        return Ok(policies.Select(p => p.ToResponse()));
    }

    /// <summary>Get a single policy by id.</summary>
    [HttpGet("policies/{id:guid}")]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var policy = await _policyService.GetByIdAsync(id, cancellationToken);
        return Ok(policy.ToResponse());
    }

    /// <summary>Update policy details. Cancelled policies cannot be updated.</summary>
    [HttpPut("policies/{id:guid}")]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyResponse>> Update(
        Guid id,
        [FromBody] UpdatePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _policyService.UpdateAsync(id, new Policy
        {
            Premium = request.Premium,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CoverageDetails = request.CoverageDetails.Trim()
        }, cancellationToken);

        return Ok(policy.ToResponse());
    }

    /// <summary>Cancel/terminate an existing policy (soft status change).</summary>
    [HttpPost("policies/{id:guid}/cancel")]
    [ProducesResponseType(typeof(PolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyResponse>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var policy = await _policyService.CancelAsync(id, cancellationToken);
        return Ok(policy.ToResponse());
    }
}
