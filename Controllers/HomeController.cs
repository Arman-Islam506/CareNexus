using System.Diagnostics;
using CareNexus.Models;
using Microsoft.AspNetCore.Mvc;

namespace CareNexus.Controllers;

public class HomeController : Controller
{
    // The website's first page sends everyone to the login page
    public IActionResult Index()
    {
        return RedirectToAction("Login", "Account");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
