using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Movimientos: entradas y salidas de inventario.</summary>
[Authorize(Roles = Roles.Empleados)]
public class MovimientosController : AppController
{
    public async Task<IActionResult> Index(int? repuestoId, TipoMovimiento? tipo, DateTime? desde, DateTime? hasta)
    {
        var url = "api/movimientos?take=300";
        if (repuestoId.HasValue) url += $"&repuestoId={repuestoId}";
        if (tipo.HasValue) url += $"&tipo={tipo}";
        if (desde.HasValue) url += $"&desde={desde:yyyy-MM-dd}";
        if (hasta.HasValue) url += $"&hasta={hasta:yyyy-MM-dd}";

        var r = await Api.GetAsync<List<MovimientoDto>>(url);
        if (!r.Ok) Fallo(r);

        var rep = await Api.GetAsync<List<RepuestoDto>>("api/repuestos");
        ViewBag.Repuestos = new SelectList((rep.Data ?? new()).Select(x => new { x.Id, Texto = $"{x.Codigo} - {x.Nombre}" }), "Id", "Texto", repuestoId);
        ViewBag.Tipo = tipo;
        ViewBag.Desde = desde?.ToString("yyyy-MM-dd");
        ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd");
        return View(r.Data ?? new());
    }
}
