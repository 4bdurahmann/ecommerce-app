using Microsoft.AspNetCore.Mvc;

namespace CartFlow.Web.Controllers {
    public class AccountController : Controller {
        public IActionResult SignIn() {
            return View();
        }

        public IActionResult SignUp() {
            return View();
        }
    }
}
