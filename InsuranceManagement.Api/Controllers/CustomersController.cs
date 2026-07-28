using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InsuranceManagement.Api.DTOs;
using InsuranceManagement.Api.Security;
using InsuranceManagement.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InsuranceManagement.Api.Controllers;

[ApiController]
[Route("api/customers")]
[EnableRateLimiting(RateLimitPolicies.PerIpBurst)]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitPolicies.WritePerIdentity)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create([FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await _customerService.CreateAsync(request.ToEntity(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer.ToResponse());
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<CustomerResponse>>> GetAll(
        [FromQuery] CustomerQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var customers = await _customerService.GetAllAsync(query.Search, query.Page, query.PageSize, query.SortBy, query.SortDir, cancellationToken);
        return Ok(customers.ToResponse(c => c.ToResponse()));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetByIdAsync(id, includePolicies: true, cancellationToken);
        return Ok(customer.ToResponse(includePolicies: true));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.CanWrite)]
    [EnableRateLimiting(RateLimitPolicies.WritePerIdentity)]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await _customerService.UpdateAsync(id, request.ToEntity(), cancellationToken);

        return Ok(customer.ToResponse());
    }
}
