using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Inventario: CRUD de repuestos, existencias, entradas/salidas y fotografía.</summary>
[Authorize(Roles = Roles.Empleados)]
public class InventarioController : AppController
{
    public async Task<IActionResult> Index(string? buscar, bool soloBajoStock = false)
    {
        ViewBag.Buscar = buscar;
        ViewBag.SoloBajoStock = soloBajoStock;
        var r = await Api.GetAsync<List<RepuestoDto>>($"api/repuestos?buscar={Uri.EscapeDataString(buscar ?? "")}&soloBajoStock={soloBajoStock}");
        if (!r.Ok) Fallo(r);
        var alertas = await Api.GetAsync<List<RepuestoDto>>("api/repuestos/alertas");
        ViewBag.Alertas = alertas.Data ?? new List<RepuestoDto>();
        return View(r.Data ?? new());
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var r = await Api.GetAsync<RepuestoDto>($"api/repuestos/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        var m = await Api.GetAsync<List<MovimientoDto>>($"api/repuestos/{id}/movimientos");
        ViewBag.Movimientos = m.Data ?? new List<MovimientoDto>();
        var o = await Api.GetAsync<List<OrdenDto>>("api/ordenes?soloActivas=true");
        ViewBag.Ordenes = o.Data ?? new List<OrdenDto>();
        return View(r.Data);
    }

    public IActionResult Crear() => View("Form", new RepuestoSaveDto());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(RepuestoSaveDto model, IFormFile? foto)
    {
        if (!ModelState.IsValid) return View("Form", model);
        var r = await Api.PostAsync<RepuestoDto>("api/repuestos", model);
        if (!r.Ok || r.Data is null) { ErroresAlModelo(r); return View("Form", model); }
        if (foto is { Length: > 0 })
        {
            var f = await Api.PostArchivoAsync<ImagenSubidaDto>($"api/repuestos/{r.Data.Id}/foto", foto);
            if (!f.Ok) Fallo("Repuesto guardado, pero la foto no: " + f.MensajeCompleto);
        }
        Exito($"Repuesto {r.Data.Codigo} registrado.");
        return RedirectToAction(nameof(Detalle), new { id = r.Data.Id });
    }

    public async Task<IActionResult> Editar(int id)
    {
        var r = await Api.GetAsync<RepuestoDto>($"api/repuestos/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        ViewBag.Id = id;
        ViewBag.FotoRuta = r.Data.FotoRuta;
        return View("Form", r.Data);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(int id, RepuestoSaveDto model, IFormFile? foto)
    {
        ViewBag.Id = id;
        if (!ModelState.IsValid) return View("Form", model);
        var r = await Api.PutAsync<RepuestoDto>($"api/repuestos/{id}", model);
        if (!r.Ok) { ErroresAlModelo(r); return View("Form", model); }
        if (foto is { Length: > 0 })
        {
            var f = await Api.PostArchivoAsync<ImagenSubidaDto>($"api/repuestos/{id}/foto", foto);
            if (!f.Ok) { Fallo("Datos guardados, pero la foto no: " + f.MensajeCompleto); return RedirectToAction(nameof(Detalle), new { id }); }
        }
        Exito("Repuesto actualizado.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await Api.DeleteAsync($"api/repuestos/{id}");
        if (r.Ok) { Exito("Repuesto eliminado (o dado de baja si tenía historial)."); return RedirectToAction(nameof(Index)); }
        Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrada(int id, MovimientoRequest model) => await Movimiento(id, model, "entradas");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salida(int id, MovimientoRequest model) => await Movimiento(id, model, "salidas");

    private async Task<IActionResult> Movimiento(int id, MovimientoRequest model, string tipo)
    {
        var r = await Api.PostAsync<MovimientoResultadoDto>($"api/repuestos/{id}/{tipo}", model);
        if (r.Ok && r.Data != null)
        {
            Exito(r.Data.Mensaje);
            if (r.Data.AlertaStockMinimo) Alerta($"Stock mínimo alcanzado: quedan {r.Data.ExistenciasActuales} unidades.");
        }
        else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Foto(int id, IFormFile? foto)
    {
        var f = await Api.PostArchivoAsync<ImagenSubidaDto>($"api/repuestos/{id}/foto", foto);
        if (f.Ok) Exito("Fotografía actualizada."); else Fallo(f);
        return RedirectToAction(nameof(Detalle), new { id });
    }
}
