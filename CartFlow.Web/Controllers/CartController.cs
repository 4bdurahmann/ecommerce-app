using CartFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace CartFlow.Web.Controllers {
    public class CartController : Controller {
        public IActionResult Index() {
            var model = new CartViewModel {
                Items = new List<CartItemViewModel> {
                    new() {
                        ProductName = "Starter Hoodie",
                        CategoryName = "Apparel",
                        UnitPrice = 49.99m,
                        Quantity = 1
                    },
                    new() {
                        ProductName = "CartFlow Mug",
                        CategoryName = "Accessories",
                        UnitPrice = 14.50m,
                        Quantity = 2
                    }
                }
            };

            return View(model);
        }
    }
}
