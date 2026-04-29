using CartFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace CartFlow.Web.Controllers {
    public class CheckoutController : Controller {
        public IActionResult Index() {
            return View(new CheckoutViewModel());
        }

        public IActionResult Confirmation() {
            return View(new OrderSummaryViewModel {
                OrderNumber = "CF-1001",
                ItemCount = 3,
                Total = 64.49m
            });
        }
    }
}
