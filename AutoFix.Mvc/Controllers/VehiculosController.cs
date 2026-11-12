using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Vehículos: CRUD + fotografía del vehículo.</summary>
public class VehiculosController : AppController
{
    public async Task<IActionResult> Index(string? buscar)
    {
        ViewBag.Buscar = buscar;
        var r = await Api.GetAsync<List<VehiculoDto>>($"api/vehiculos?buscar={Uri.EscapeDataString(buscar ?? "")}");
        if (!r.Ok) Fallo(r);
        return View(r.Data ?? new());
    }

    public async Task<IActionResult> Detalle(int id)
    {
        var r = await Api.GetAsync<VehiculoDto>($"api/vehiculos/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        var ordenes = await Api.GetAsync<List<OrdenDto>>($"api/ordenes?vehiculoId={id}");
        ViewBag.Ordenes = ordenes.Data ?? new List<OrdenDto>();
        return View(r.Data);
    }

    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Crear(int? clienteId)
    {
        await CargarClientes(clienteId);
        return View("Form", new VehiculoSaveDto { ClienteId = clienteId ?? 0 });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Crear(VehiculoSaveDto model, IFormFile? foto, string? siguiente)
    {
        if (!ModelState.IsValid) { await CargarClientes(model.ClienteId); return View("Form", model); }

        var r = await Api.PostAsync<VehiculoDto>("api/vehiculos", model);
        if (!r.Ok || r.Data is null) { ErroresAlModelo(r); await CargarClientes(model.ClienteId); return View("Form", model); }

        if (foto is { Length: > 0 })
        {
            var f = await Api.PostArchivoAsync<ImagenSubidaDto>($"api/vehiculos/{r.Data.Id}/foto", foto);
            if (!f.Ok) Fallo("Vehículo guardado, pero la foto no: " + f.MensajeCompleto);
        }
        Exito($"Vehículo {r.Data.Placa} registrado.");

        // "Guardar y agregar otro" facilita registrar un cliente con dos vehículos (prueba 1).
        return siguiente == "otro"
            ? RedirectToAction(nameof(Crear), new { clienteId = model.ClienteId })
            : RedirectToAction("Detalle", "Clientes", new { id = model.ClienteId });
    }

    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Editar(int id)
    {
        var r = await Api.GetAsync<VehiculoDto>($"api/vehiculos/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        ViewBag.Id = id;
        ViewBag.FotoRuta = r.Data.FotoRuta;
        await CargarClientes(r.Data.ClienteId);
        return View("Form", r.Data);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Editar(int id, VehiculoSaveDto model, IFormFile? foto)
    {
        ViewBag.Id = id;
        if (!ModelState.IsValid) { await CargarClientes(model.ClienteId); return View("Form", model); }
        var r = await Api.PutAsync<VehiculoDto>($"api/vehiculos/{id}", model);
        if (!r.Ok) { ErroresAlModelo(r); await CargarClientes(model.ClienteId); return View("Form", model); }

        if (foto is { Length: > 0 })
        {
            var f = await Api.PostArchivoAsync<ImagenSubidaDto>($"api/vehiculos/{id}/foto", foto);
            if (!f.Ok) { Fallo("Datos guardados, pero la foto no: " + f.MensajeCompleto); return RedirectToAction(nameof(Detalle), new { id }); }
        }
        Exito("Vehículo actualizado.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Foto(int id, IFormFile? foto)
    {
        var f = await Api.PostArchivoAsync<ImagenSubidaDto>($"api/vehiculos/{id}/foto", foto);
        if (f.Ok) Exito("Fotografía actualizada."); else Fallo(f);
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await Api.DeleteAsync($"api/vehiculos/{id}");
        if (r.Ok) Exito("Vehículo eliminado."); else Fallo(r);
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarClientes(int? seleccionado)
    {
        var c = await Api.GetAsync<List<ClienteDto>>("api/clientes");
        ViewBag.Clientes = new SelectList((c.Data ?? new()).Select(x => new { x.Id, Texto = $"{x.NombreCompleto} ({x.Dui})" }),
            "Id", "Texto", seleccionado);
    }
}
