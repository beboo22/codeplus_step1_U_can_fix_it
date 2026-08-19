using ECommerce.API.DTOs;
using ECommerce.DAL.Context;
using ECommerce.DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _context;

    public OrdersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();
        return Ok(order);
    }

    [HttpGet("customer/{customerId}")]
    public async Task<ActionResult<List<Order>>> GetCustomerOrders(int customerId)
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPost("cancel/{id}")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound("Order not found");
        if (order.Status == OrderStatus.Cancelled) return BadRequest("Order is already cancelled");

        if (order.Status == OrderStatus.Paid)
        {
            foreach (var item in order.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product != null)
                {
                    product.StockQuantity += item.Quantity;
                }
            }
        }

        order.Status = OrderStatus.Cancelled;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Order cancelled successfully" });
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CreateOrderDto request)
    {
        if (request.Items == null || !request.Items.Any())
        {
            return BadRequest("Cannot checkout an empty order.");
        }

        var customer = await _context.Customers.FindAsync(request.CustomerId);
        if (customer == null)
        {
            return NotFound($"Customer with ID {request.CustomerId} not found.");
        }

        decimal subtotal = 0m;
        var orderItemsToSave = new List<OrderItem>();
        var productsToUpdate = new List<Product>();

        foreach (var itemDto in request.Items)
        {
            if (itemDto.Quantity <= 0)
            {
                return BadRequest("Product quantity must be at least 1.");
            }

            var product = await _context.Products.FindAsync(itemDto.ProductId);
            if (product == null)
            {
                return NotFound($"Product with ID {itemDto.ProductId} not found.");
            }

            if (product.StockQuantity < itemDto.Quantity)
            {
                return BadRequest($"Insufficient stock for product '{product.Name}'. Available: {product.StockQuantity}, Requested: {itemDto.Quantity}");
            }

            subtotal += product.Price * itemDto.Quantity;

            orderItemsToSave.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = itemDto.Quantity,
                UnitPrice = product.Price
            });

            product.StockQuantity -= itemDto.Quantity;
            productsToUpdate.Add(product);
        }

        decimal discount = 0m;
        if (customer.IsVip)
        {
            discount += Math.Round(subtotal * 0.15m, 2);
        }

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var coupon = await _context.Coupons
                .FirstOrDefaultAsync(c => c.Code.ToUpper() == request.CouponCode.ToUpper() && c.IsActive);

            if (coupon != null)
            {
                discount += Math.Round(subtotal * (coupon.DiscountPercentage / 100m), 2);
            }
            else
            {
                return BadRequest($"Invalid or inactive coupon code '{request.CouponCode}'.");
            }
        }

        if (discount > subtotal)
        {
            discount = subtotal;
        }

        var netAmount = subtotal - discount;
        var tax = Math.Round(netAmount * 0.14m, 2);
        var shipping = netAmount >= 1000m ? 0m : 75m;
        var finalTotal = netAmount + tax + shipping;

        if (finalTotal > 50000m)
        {
            return BadRequest("Payment processing failed. Amount exceeds limit.");
        }

        var txRef = $"TX-LEGACY-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

        var order = new Order
        {
            CustomerId = customer.Id,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.Paid,
            Subtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = tax,
            ShippingFee = shipping,
            TotalAmount = finalTotal,
            Items = orderItemsToSave
        };

        var payment = new Payment
        {
            Order = order,
            Amount = finalTotal,
            PaymentDate = DateTime.UtcNow,
            TransactionReference = txRef,
            IsSuccess = true
        };

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Orders.AddAsync(order);
            await _context.Payments.AddAsync(payment);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, "An error occurred while saving the order.");
        }

        return Ok(new
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Subtotal = order.Subtotal,
            Discount = order.DiscountAmount,
            Tax = order.TaxAmount,
            Shipping = order.ShippingFee,
            Total = order.TotalAmount,
            TransactionReference = txRef
        });
    }
}
