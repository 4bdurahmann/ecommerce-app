using CartFlow.Data.Data;
using CartFlow.Data.Entities;
using CartFlow.Data.Enums;
using CartFlow.Services.Interfaces;
using CartFlow.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CartFlow.Web.Controllers
{
    [Authorize]
    public class CheckoutController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IPaymentService _paymentService;
        private readonly IConfiguration _configuration;

        public CheckoutController(AppDbContext context, IPaymentService paymentService, IConfiguration configuration)
        {
            _context = context;
            _paymentService = paymentService;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? productId, int? quantity)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = int.Parse(userIdString);
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            // If a single product is passed (Buy Now flow), prepare checkout items from that product
            if (productId.HasValue)
            {
                var prod = await _context.Products
                    .Include(p => p.ProductImages)
                    .FirstOrDefaultAsync(p => p.Id == productId.Value);

                if (prod == null)
                {
                    return NotFound();
                }

                var qty = (quantity.HasValue && quantity.Value > 0) ? quantity.Value : 1;

                ViewBag.ProductId = productId.Value;
                ViewBag.Quantity = qty;
                ViewBag.CartItems = new List<CartItemViewModel>
                {
                    new CartItemViewModel
                    {
                        Id = 0,
                        ProductId = prod.Id,
                        ProductName = prod.Name,
                        UnitPrice = prod.UnitPrice,
                        Quantity = qty,
                        ImageUrl = prod.ProductImages?.FirstOrDefault(pi => pi.IsPrimary)?.Image
                                   ?? prod.ProductImages?.FirstOrDefault()?.Image
                                   ?? string.Empty
                    }
                };
            }
            else
            {
                var cart = await _context.Carts
                    .Include(c => c.CartItems)
                        .ThenInclude(ci => ci.Product)
                            .ThenInclude(p => p.ProductImages)
                    .FirstOrDefaultAsync(c => c.UserId == userId);

                if (cart == null || !cart.CartItems.Any())
                {
                    return RedirectToAction("Index", "Cart");
                }

                ViewBag.CartItems = cart.CartItems.Select(ci => new CartItemViewModel
                {
                    Id = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = ci.Product.Name,
                    UnitPrice = ci.UnitPrice,
                    Quantity = ci.Quantity,
                    ImageUrl = ci.Product.ProductImages?.FirstOrDefault(pi => pi.IsPrimary)?.Image
                        ?? ci.Product.ProductImages?.FirstOrDefault()?.Image
                        ?? string.Empty
                }).ToList();
            }

            var viewModel = new CheckoutViewModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                ProductId = ViewBag.ProductId as int?,
                Quantity = ViewBag.Quantity as int? ?? 1
            };

            ViewBag.StripePublishableKey = _configuration["StripeKeys:PublishableKey"];
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(CheckoutViewModel model)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString))
            {
                return RedirectToAction("Login", "Account");
            }

            int userId = int.Parse(userIdString);

            if (!ModelState.IsValid)
            {
                await PopulateCartItemsForPost(model);
                return View(model);
            }

            List<(Product Product, int Quantity, decimal UnitPrice)> checkoutItems;
            var qty = model.Quantity > 0 ? model.Quantity : 1;

            if (model.ProductId.HasValue)
            {
                var prod = await _context.Products.Include(p => p.ProductImages).FirstOrDefaultAsync(p => p.Id == model.ProductId.Value);
                if (prod == null)
                {
                    return NotFound();
                }
                if (prod.StockQuantity < qty)
                {
                    ModelState.AddModelError("", $"Sorry, the product '{prod.Name}' does not have enough stock. Available: {prod.StockQuantity}");
                    await PopulateCartItemsForPost(model);
                    return View(model);
                }
                checkoutItems = new() { (prod, qty, prod.UnitPrice) };
            }
            else
            {
                var cart = await _context.Carts
                    .Include(c => c.CartItems)
                        .ThenInclude(ci => ci.Product)
                    .FirstOrDefaultAsync(c => c.UserId == userId);

                if (cart == null || !cart.CartItems.Any())
                {
                    return RedirectToAction("Index", "Cart");
                }

                foreach (var item in cart.CartItems)
                {
                    if (item.Product.StockQuantity < item.Quantity)
                    {
                        ModelState.AddModelError("", $"Sorry, the product '{item.Product.Name}' does not have enough stock. Available: {item.Product.StockQuantity}");
                        await PopulateCartItemsForPost(model);
                        return View(model);
                    }
                }

                checkoutItems = cart.CartItems.Select(ci => (ci.Product, ci.Quantity, ci.UnitPrice)).ToList();
            }

            decimal subtotal = checkoutItems.Sum(item => item.Quantity * item.UnitPrice);
            decimal shipping = 50.00m;
            decimal totalPrice = subtotal + shipping;
            int totalQuantity = checkoutItems.Sum(item => item.Quantity);

            PaymentMethod paymentMethod = model.PaymentMethod == "CreditCard" ? PaymentMethod.Credit : PaymentMethod.Cash;

            string? stripePaymentIntentId = null;

            if (paymentMethod == PaymentMethod.Credit)
            {
                if (string.IsNullOrEmpty(model.PaymentMethodId))
                {
                    ModelState.AddModelError("", "Credit card information is required.");
                    await PopulateCartItemsForPost(model);
                    return View(model);
                }

                var paymentResult = await _paymentService.CreatePaymentIntentAsync(totalPrice, "egp", model.PaymentMethodId);

                if (!paymentResult.Success)
                {
                    ModelState.AddModelError("", $"Payment failed: {paymentResult.ErrorMessage}");
                    await PopulateCartItemsForPost(model);
                    return View(model);
                }

                stripePaymentIntentId = paymentResult.PaymentIntentId;
            }

            var order = new Order
            {
                UserId = userId,
                OrderDate = DateTime.UtcNow,
                OrderStatus = Status.Ordered,
                TotalQuantity = totalQuantity,
                TotalPrice = totalPrice,
                PaymentMethod = paymentMethod,
                StripePaymentIntentId = stripePaymentIntentId,
                AddressLine1 = model.AddressLine1,
                City = model.City,
                State = model.State,
                PostalCode = model.PostalCode,
                Country = model.Country
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            foreach (var (prod, itemQty, price) in checkoutItems)
            {
                var orderItem = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = prod.Id,
                    Quantity = itemQty,
                    Price = price
                };
                _context.OrderItems.Add(orderItem);

                prod.StockQuantity -= itemQty;
            }

            if (!model.ProductId.HasValue)
            {
                var cart = await _context.Carts.FirstOrDefaultAsync(c => c.UserId == userId);
                if (cart != null)
                {
                    _context.CartItems.RemoveRange(
                        _context.CartItems.Where(ci => ci.CartId == cart.Id));
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Confirmation), new { id = order.Id });
        }

        private async Task PopulateCartItemsForPost(CheckoutViewModel model)
        {
            if (model.ProductId.HasValue)
            {
                var prod = await _context.Products.Include(p => p.ProductImages)
                    .FirstOrDefaultAsync(p => p.Id == model.ProductId.Value);
                if (prod != null)
                {
                    var qty = model.Quantity > 0 ? model.Quantity : 1;
                    ViewBag.CartItems = new List<CartItemViewModel>
                    {
                        new()
                        {
                            Id = 0,
                            ProductId = prod.Id,
                            ProductName = prod.Name,
                            UnitPrice = prod.UnitPrice,
                            Quantity = qty,
                            ImageUrl = prod.ProductImages?.FirstOrDefault(pi => pi.IsPrimary)?.Image
                                       ?? prod.ProductImages?.FirstOrDefault()?.Image
                                       ?? string.Empty
                        }
                    };
                }
            }
            else
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdString, out var userId))
                {
                    var cart = await _context.Carts
                        .Include(c => c.CartItems)
                            .ThenInclude(ci => ci.Product)
                                .ThenInclude(p => p.ProductImages)
                        .FirstOrDefaultAsync(c => c.UserId == userId);

                    if (cart != null)
                    {
                        ViewBag.CartItems = cart.CartItems.Select(ci => new CartItemViewModel
                        {
                            Id = ci.Id,
                            ProductId = ci.ProductId,
                            ProductName = ci.Product.Name,
                            UnitPrice = ci.UnitPrice,
                            Quantity = ci.Quantity,
                            ImageUrl = ci.Product.ProductImages?.FirstOrDefault(pi => pi.IsPrimary)?.Image
                                       ?? ci.Product.ProductImages?.FirstOrDefault()?.Image
                                       ?? string.Empty
                        }).ToList();
                    }
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> Confirmation(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            var summaryViewModel = new OrderSummaryViewModel
            {
                OrderNumber = order.Id.ToString(),
                ItemCount = order.TotalQuantity,
                Total = order.TotalPrice,
                PaymentMethod = order.PaymentMethod == PaymentMethod.Credit ? "Credit Card" : "Cash on Delivery"
            };

            return View(summaryViewModel);
        }
    }
}
