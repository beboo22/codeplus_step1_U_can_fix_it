using ECommerce.API.DTOs;
using ECommerce.DAL.Context;
using ECommerce.DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProductsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<Product>>> GetAll()
    {
        var products = await _context.Products.ToListAsync();
        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetById(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) 
            return NotFound($"Product with ID {id} not found.");
            
        return Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create([FromBody] CreateProductDto dto)
    {
        if (dto.Price <= 0)
        {
            return BadRequest("Product price must be greater than zero.");
        }

        if (dto.StockQuantity < 0)
        {
            return BadRequest("Stock quantity cannot be negative.");
        }

        var skuExists = await _context.Products.AnyAsync(p => p.SKU.ToLower() == dto.SKU.ToLower());
        if (skuExists)
        {
            return BadRequest($"Product with SKU '{dto.SKU}' already exists.");
        }

        var product = new Product
        {
            Name = dto.Name,
            SKU = dto.SKU.ToUpper(),
            Price = dto.Price,
            StockQuantity = dto.StockQuantity
        };

        await _context.Products.AddAsync(product);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Product product)
    {
        var existing = await _context.Products.FindAsync(id);
        if (existing == null) 
            return NotFound($"Product with ID {id} not found.");

        if (product.Price <= 0)
            return BadRequest("Price must be positive.");

        existing.Name = product.Name;
        existing.SKU = product.SKU;
        existing.Price = product.Price;
        existing.StockQuantity = product.StockQuantity;

        _context.Products.Update(existing);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) 
            return NotFound($"Product with ID {id} not found.");

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
