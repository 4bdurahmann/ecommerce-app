using CartFlow.Data.Entities;
using Microsoft.AspNetCore.Mvc;

namespace CartFlow.Web.Controllers {
    public class ProductsController : Controller {
        private static readonly List<Product> Products = new() {
            new Product {
                Id = 1,
                Name = "Starter Hoodie",
                Description = "A simple sample product to trace the Product entity end to end.",
                StockQuantity = 12,
                UnitPrice = 49.99m,
                Category = new Category { Id = 1, Name = "Apparel" }
            },
            new Product {
                Id = 2,
                Name = "CartFlow Mug",
                Description = "A second sample product for the listing page.",
                StockQuantity = 30,
                UnitPrice = 14.50m,
                Category = new Category { Id = 2, Name = "Accessories" }
            }
        };

        public IActionResult Index() {
            return View(Products);
        }

        public IActionResult Details(int id) {
            var product = Products.FirstOrDefault(p => p.Id == id);
            if (product is null) {
                return NotFound();
            }

            return View(product);
        }
    }
}
