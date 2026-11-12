using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Presupuestos: crear, enviar a aprobación, aprobar y rechazar.</summary>
public class PresupuestosController : AppController
{
    public async Task<IActionResult> Index(EstadoPresupuesto? estado)
    {
        ViewBag.Estado = estado;
        var r = await Api.GetAsync<List<PresupuestoDto>>("api/presupuestos" + (estado.HasValue ? $"?estado={estado}" : ""));
        if (!r.Ok) Fallo(r);
        return View(r.Data ?? new());
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var r = await Api.GetAsync<PresupuestoDto>($"api/presupuestos/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        return View(r.Data);
    }

    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Crear(int ordenId)
    {
        var o = await Api.GetAsync<OrdenDetalleDto>($"api/ordenes/{ordenId}");
        if (!o.Ok || o.Data is null) return ErrorDeCarga(o);

        var model = new CrearPresupuestoRequest();
        if (o.Data.Presupuesto != null)
        {
            model.ManoObra = o.Data.Presupuesto.ManoObra;
            model.Observaciones = o.Data.Presupuesto.Observaciones;
            model.Repuestos = o.Data.Presupuesto.Detalles.Select(d => new LineaPresupuestoRequest
            {
                RepuestoId = d.RepuestoId, Cantidad = d.Cantidad, PrecioUnitario = d.PrecioUnitario
            }).ToList();
        }
        await CargarDatos(o.Data);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Crear(int ordenId, CrearPresupuestoRequest model)
    {
        model.Repuestos = model.Repuestos.Where(l => l.RepuestoId > 0).ToList();
        var r = await Api.PostAsync<PresupuestoDto>($"api/ordenes/{ordenId}/presupuesto", model);
        if (!r.Ok || r.Data is null)
        {
            ErroresAlModelo(r);
            var o = await Api.GetAsync<OrdenDetalleDto>($"api/ordenes/{ordenId}");
            if (o.Data is null) return ErrorDeCarga(o);
            await CargarDatos(o.Data);
            return View(model);
        }
        Exito($"Presupuesto guardado. Total: {Services.Ui.Dinero(r.Data.Total)}");
        return RedirectToAction(nameof(Detalle), new { id = r.Data.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Enviar(int id)
    {
        var r = await Api.PostAsync<PresupuestoDto>($"api/presupuestos/{id}/enviar", new ComentarioRequest());
        if (r.Ok) Exito("Presupuesto enviado a aprobación."); else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprobar(int id, string? comentario)
    {
        var r = await Api.PostAsync<PresupuestoDto>($"api/presupuestos/{id}/aprobar", new ComentarioRequest { Comentario = comentario });
        if (r.Ok) Exito("Presupuesto APROBADO. La orden pasó a estado «Aprobado»."); else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rechazar(int id, string? comentario)
    {
        var r = await Api.PostAsync<PresupuestoDto>($"api/presupuestos/{id}/rechazar", new ComentarioRequest { Comentario = comentario });
        if (r.Ok) Exito("Presupuesto rechazado. La orden regresó a «Presupuestado»."); else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    private async Task CargarDatos(OrdenDetalleDto orden)
    {
        ViewBag.Orden = orden;
        var rep = await Api.GetAsync<List<RepuestoDto>>("api/repuestos");
        ViewBag.Repuestos = rep.Data ?? new List<RepuestoDto>();
    }
}
