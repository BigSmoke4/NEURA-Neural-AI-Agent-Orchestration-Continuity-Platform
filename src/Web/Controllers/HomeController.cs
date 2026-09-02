using Microsoft.AspNetCore.Mvc;

namespace Neura.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Error() => View();
}
