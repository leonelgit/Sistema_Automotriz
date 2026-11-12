using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Clientes: CRUD de clientes (vía API).</summary>
public class ClientesController : AppController
{
    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Index(string? buscar)
    {
        ViewBag.Buscar = buscar;
        var r = await Api.GetAsync<List<ClienteDto>>($"api/clientes?buscar={Uri.EscapeDataString(buscar ?? "")}");
        if (!r.Ok) Fallo(r);
        return View(r.Data ?? new());
    }

    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Detalle(int id)
    {
        var r = await Api.GetAsync<ClienteDto>($"api/clientes/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        var ordenes = await Api.GetAsync<List<OrdenDto>>($"api/ordenes?clienteId={id}");
        ViewBag.Ordenes = ordenes.Data ?? new List<OrdenDto>();
        return View(r.Data);
    }

    /// <summary>El cliente autenticado ve sus datos.</summary>
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> Mio()
    {
        var r = await Api.GetAsync<ClienteDto>("api/clientes/mio");
        if (!r.Ok || r.Data is null) { Fallo(r); return RedirectToAction("Index", "Home"); }
        var ordenes = await Api.GetAsync<List<OrdenDto>>("api/ordenes");
        ViewBag.Ordenes = ordenes.Data ?? new List<OrdenDto>();
        return View("Detalle", r.Data);
    }

    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public IActionResult Crear() => View("Form", new ClienteSaveDto());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Crear(ClienteSaveDto model)
    {
        if (!ModelState.IsValid) return View("Form", model);
        var r = await Api.PostAsync<ClienteDto>("api/clientes", model);
        if (!r.Ok || r.Data is null) { ErroresAlModelo(r); return View("Form", model); }
        Exito($"Cliente {r.Data.NombreCompleto} registrado. Ahora agregue sus vehículos.");
        return RedirectToAction(nameof(Detalle), new { id = r.Data.Id });
    }

    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Editar(int id)
    {
        var r = await Api.GetAsync<ClienteDto>($"api/clientes/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        ViewBag.Id = id;
        return View("Form", r.Data);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<IActionResult> Editar(int id, ClienteSaveDto model)
    {
        ViewBag.Id = id;
        if (!ModelState.IsValid) return View("Form", model);
        var r = await Api.PutAsync<ClienteDto>($"api/clientes/{id}", model);
        if (!r.Ok) { ErroresAlModelo(r); return View("Form", model); }
        Exito("Cliente actualizado.");
        return RedirectToAction(nameof(Detalle), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Empleados)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await Api.DeleteAsync($"api/clientes/{id}");
        if (r.Ok) Exito("Cliente eliminado."); else Fallo(r);
        return RedirectToAction(nameof(Index));
    }
}
