using AutoFix.Contracts;
using AutoFix.Mvc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vistas Órdenes (gestión) y Detalle de orden (diagnóstico y seguimiento).</summary>
public class OrdenesController : AppController
{
    public async Task<IActionResult> Index(EstadoOrden? estado, string? buscar, bool soloActivas = false)
    {
        ViewBag.Estado = estado;
        ViewBag.Buscar = buscar;
        ViewBag.SoloActivas = soloActivas;
        var url = $"api/ordenes?buscar={Uri.EscapeDataString(buscar ?? "")}&soloActivas={soloActivas}";
        if (estado.HasValue) url += $"&estado={estado}";
        var r = await Api.GetAsync<List<OrdenDto>>(url);
        if (!r.Ok) Fallo(r);
        return View(r.Data ?? new());
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var r = await Api.GetAsync<OrdenDetalleDto>($"api/ordenes/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);

        if (User.EsAlguno(Roles.Administrador, Roles.Supervisor))
        {
            var m = await Api.GetAsync<List<MecanicoDto>>("api/mecanicos?soloActivos=true");
            ViewBag.Mecanicos = m.Data ?? new List<MecanicoDto>();
        }
        return View(r.Data);
    }

    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Crear(int? vehiculoId)
    {
        await CargarVehiculos(vehiculoId);
        return View(new CrearOrdenRequest { VehiculoId = vehiculoId ?? 0 });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Crear(CrearOrdenRequest model, IFormFile? fotoIngreso)
    {
        if (!ModelState.IsValid) { await CargarVehiculos(model.VehiculoId); return View(model); }

        var r = await Api.PostAsync<OrdenDetalleDto>("api/ordenes", model);
        if (!r.Ok || r.Data is null) { ErroresAlModelo(r); await CargarVehiculos(model.VehiculoId); return View(model); }

        // Fotografía de recepción opcional
        if (fotoIngreso is { Length: > 0 })
        {
            var f = await Api.PostArchivoAsync<EvidenciaDto>($"api/ordenes/{r.Data.Id}/evidencias", fotoIngreso,
                new() { ["tipo"] = nameof(TipoEvidencia.Ingreso), ["descripcion"] = "Vehículo al ingresar" });
            if (!f.Ok) Fallo("Orden creada, pero la foto no se guardó: " + f.MensajeCompleto);
        }
        Exito($"Orden {r.Data.NumeroOrden} creada en estado Recibido.");
        return RedirectToAction(nameof(Detalle), new { id = r.Data.Id });
    }

    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Editar(int id)
    {
        var r = await Api.GetAsync<OrdenDetalleDto>($"api/ordenes/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        ViewBag.Orden = r.Data;
        return View(new EditarOrdenRequest
        {
            DescripcionProblema = r.Data.DescripcionProblema, Diagnostico = r.Data.Diagnostico,
            FechaEstimadaFin = r.Data.FechaEstimadaFin, Version = r.Data.Version
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(int id, EditarOrdenRequest model)
    {
        if (!ModelState.IsValid) return await Editar(id);
        var r = await Api.PutAsync<OrdenDetalleDto>($"api/ordenes/{id}", model);
        if (!r.Ok) { Fallo(r); return RedirectToAction(nameof(Detalle), new { id }); }
        Exito("Orden actualizada.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(int id, EstadoOrden nuevoEstado, Guid? version, string? comentario)
    {
        var r = await Api.PostAsync<OrdenDetalleDto>($"api/ordenes/{id}/estado",
            new CambiarEstadoRequest { NuevoEstado = nuevoEstado, Version = version, Comentario = comentario });
        if (r.Ok)
            Exito(nuevoEstado == EstadoOrden.EnReparacion
                ? "Reparación iniciada. Los repuestos del presupuesto se descontaron del inventario."
                : $"La orden pasó a «{nuevoEstado.Texto()}».");
        else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Asignar(int id, AsignarMecanicoRequest model)
    {
        var r = await Api.PostAsync<AsignacionDto>($"api/ordenes/{id}/asignar", model);
        if (r.Ok && r.Data != null) Exito($"Mecánico {r.Data.MecanicoNombre} asignado ({Ui.Fecha(r.Data.Inicio)} – {r.Data.Fin:HH:mm}).");
        else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalizar(int id, string? comentario)
    {
        var r = await Api.PostAsync<OrdenDetalleDto>($"api/ordenes/{id}/finalizar", new ComentarioRequest { Comentario = comentario });
        if (r.Ok) Exito("Orden finalizada."); else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(int id, string? comentario)
    {
        var r = await Api.PostAsync<OrdenDetalleDto>($"api/ordenes/{id}/cancelar", new ComentarioRequest { Comentario = comentario });
        if (r.Ok) Exito("Orden cancelada."); else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await Api.DeleteAsync($"api/ordenes/{id}");
        if (r.Ok) { Exito("Orden eliminada."); return RedirectToAction(nameof(Index)); }
        Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubirEvidencia(int id, IFormFile? archivo, TipoEvidencia tipo, string? descripcion)
    {
        var r = await Api.PostArchivoAsync<EvidenciaDto>($"api/ordenes/{id}/evidencias", archivo,
            new() { ["tipo"] = tipo.ToString(), ["descripcion"] = descripcion });
        if (r.Ok) Exito("Fotografía guardada."); else Fallo(r);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    private async Task CargarVehiculos(int? seleccionado)
    {
        var v = await Api.GetAsync<List<VehiculoDto>>("api/vehiculos");
        ViewBag.Vehiculos = new SelectList((v.Data ?? new()).Select(x => new { x.Id, Texto = $"{x.Descripcion} — {x.ClienteNombre}" }),
            "Id", "Texto", seleccionado);
    }
}
