using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Reportes: operaciones del taller, mecánicos, inventario y logs (auditoría).</summary>
[Authorize(Roles = Roles.Administrador + "," + Roles.Supervisor)]
public class ReportesController : AppController
{
    public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta, EstadoOrden? estado)
    {
        var d = desde ?? DateTime.Today.AddDays(-30);
        var h = hasta ?? DateTime.Today;
        var url = $"api/reportes/ordenes?desde={d:yyyy-MM-dd}&hasta={h:yyyy-MM-dd}";
        if (estado.HasValue) url += $"&estado={estado}";

        var ordenes = await Api.GetAsync<ReporteOrdenesDto>(url);
        if (!ordenes.Ok) { Fallo(ordenes); return RedirectToAction("Index", "Home"); }
        var mecanicos = await Api.GetAsync<List<ReporteMecanicoDto>>("api/reportes/mecanicos");
        var inventario = await Api.GetAsync<ReporteInventarioDto>("api/reportes/inventario");

        ViewBag.Estado = estado;
        ViewBag.Mecanicos = mecanicos.Data ?? new List<ReporteMecanicoDto>();
        ViewBag.Inventario = inventario.Data ?? new ReporteInventarioDto();
        return View(ordenes.Data!);
    }

    public async Task<IActionResult> Logs(string? correlationId, string? usuario, int? codigo, bool soloErrores = false)
    {
        var url = $"api/logs?take=200&soloErrores={soloErrores}";
        if (!string.IsNullOrWhiteSpace(correlationId)) url += $"&correlationId={Uri.EscapeDataString(correlationId)}";
        if (!string.IsNullOrWhiteSpace(usuario)) url += $"&usuario={Uri.EscapeDataString(usuario)}";
        if (codigo.HasValue) url += $"&codigo={codigo}";

        var r = await Api.GetAsync<List<LogDto>>(url);
        if (!r.Ok) Fallo(r);
        ViewBag.CorrelationId = correlationId;
        ViewBag.Usuario = usuario;
        ViewBag.Codigo = codigo;
        ViewBag.SoloErrores = soloErrores;
        return View(r.Data ?? new());
    }
}
