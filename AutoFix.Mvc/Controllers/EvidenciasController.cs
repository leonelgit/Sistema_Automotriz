using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Evidencias: galería de fotografías de reparaciones y subida de nuevas.</summary>
[Authorize(Roles = Roles.Empleados)]
public class EvidenciasController : AppController
{
    public async Task<IActionResult> Index(TipoEvidencia? tipo, int? ordenId)
    {
        var url = "api/evidencias?take=200";
        if (tipo.HasValue) url += $"&tipo={tipo}";
        if (ordenId.HasValue) url += $"&ordenId={ordenId}";
        var r = await Api.GetAsync<List<EvidenciaDto>>(url);
        if (!r.Ok) Fallo(r);

        var o = await Api.GetAsync<List<OrdenDto>>("api/ordenes");
        ViewBag.Ordenes = new SelectList((o.Data ?? new()).Select(x => new { x.Id, Texto = $"{x.NumeroOrden} · {x.VehiculoDescripcion}" }), "Id", "Texto", ordenId);
        ViewBag.Tipo = tipo;
        ViewBag.OrdenId = ordenId;
        return View(r.Data ?? new());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subir(int ordenId, IFormFile? archivo, TipoEvidencia tipo, string? descripcion)
    {
        if (ordenId <= 0) { Fallo("Seleccione la orden."); return RedirectToAction(nameof(Index)); }
        var r = await Api.PostArchivoAsync<EvidenciaDto>($"api/ordenes/{ordenId}/evidencias", archivo,
            new() { ["tipo"] = tipo.ToString(), ["descripcion"] = descripcion });
        if (r.Ok) Exito($"Fotografía guardada en {r.Data?.Ruta}."); else Fallo(r);
        return RedirectToAction(nameof(Index), new { ordenId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await Api.DeleteAsync($"api/evidencias/{id}");
        if (r.Ok) Exito("Evidencia eliminada."); else Fallo(r);
        return Volver();
    }
}
