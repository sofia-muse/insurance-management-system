using InsuranceManagement.Api.DTOs;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace InsuranceManagement.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    /// <summary>Onboard a new customer (policyholder).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerService.CreateAsync(new Customer
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            DateOfBirth = request.DateOfBirth,
            Address = request.Address.Trim()
        }, cancellationToken);

        var response = customer.ToResponse();
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, response);
    }

    /// <summary>List customers. Optional search filters by name or email.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CustomerResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CustomerResponse>>> GetAll(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var customers = await _customerService.GetAllAsync(search, cancellationToken);
        return Ok(customers.Select(c => c.ToResponse()));
    }

    /// <summary>Get a customer by id, including their policies.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetByIdAsync(id, includePolicies: true, cancellationToken);
        return Ok(customer.ToResponse(includePolicies: true));
    }

    /// <summary>Update customer contact and personal details.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerService.UpdateAsync(id, new Customer
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            DateOfBirth = request.DateOfBirth,
            Address = request.Address.Trim()
        }, cancellationToken);

        return Ok(customer.ToResponse());
    }
}
