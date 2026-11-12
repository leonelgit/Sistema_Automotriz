using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Dashboard: resumen del taller.</summary>
public class HomeController : AppController
{
    public async Task<IActionResult> Index()
    {
        var r = await Api.GetAsync<DashboardDto>("api/reportes/dashboard");
        if (!r.Ok) Fallo(r);
        return View(r.Data ?? new DashboardDto());
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
