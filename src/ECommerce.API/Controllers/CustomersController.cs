using ECommerce.API.DTOs;
using ECommerce.DAL.Context;
using ECommerce.DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _context;

    public CustomersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Customer>> GetById(int id)
    {
        var customer = await _context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null) 
            return NotFound($"Customer with ID {id} not found.");

        return Ok(customer);
    }

    [HttpPost]
    public async Task<ActionResult<Customer>> Create([FromBody] CreateCustomerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
            return BadRequest("Full name is required.");

        if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains("@"))
            return BadRequest("A valid email address is required.");

        var emailExists = await _context.Customers.AnyAsync(c => c.Email.ToLower() == dto.Email.ToLower());
        if (emailExists)
        {
            return BadRequest("Email is already registered.");
        }

        var customer = new Customer
        {
            FullName = dto.FullName,
            Email = dto.Email,
            IsVip = dto.IsVip
        };

        await _context.Customers.AddAsync(customer);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
    }

    [HttpPost("{id}/upgrade-vip")]
    public async Task<IActionResult> UpgradeToVip(int id)
    {
        var customer = await _context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null) 
            return NotFound();

        var totalSpent = customer.Orders
            .Where(o => o.Status == OrderStatus.Paid)
            .Sum(o => o.TotalAmount);

        if (totalSpent < 500m)
        {
            return BadRequest($"Customer does not qualify for VIP. Total spend {totalSpent:C} is less than required $500.00");
        }

        customer.IsVip = true;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Customer upgraded to VIP successfully." });
    }
}
