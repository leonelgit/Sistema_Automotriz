using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Mecánicos: administración y agenda de asignaciones.</summary>
[Authorize(Roles = Roles.Empleados)]
public class MecanicosController : AppController
{
    public async Task<IActionResult> Index()
    {
        var r = await Api.GetAsync<List<MecanicoDto>>("api/mecanicos");
        if (!r.Ok) Fallo(r);
        return View(r.Data ?? new());
    }

    public async Task<IActionResult> Agenda(int id)
    {
        var m = await Api.GetAsync<MecanicoDto>($"api/mecanicos/{id}");
        if (!m.Ok || m.Data is null) return ErrorDeCarga(m);
        var a = await Api.GetAsync<List<AsignacionDto>>($"api/mecanicos/{id}/asignaciones");
        ViewBag.Asignaciones = a.Data ?? new List<AsignacionDto>();
        return View(m.Data);
    }

    [Authorize(Roles = Roles.Administrador)]
    public IActionResult Crear() => View("Form", new MecanicoSaveDto());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Crear(MecanicoSaveDto model)
    {
        if (!ModelState.IsValid) return View("Form", model);
        var r = await Api.PostAsync<MecanicoDto>("api/mecanicos", model);
        if (!r.Ok) { ErroresAlModelo(r); return View("Form", model); }
        Exito("Mecánico registrado. Puede crearle un usuario en Usuarios (rol Mecánico).");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id)
    {
        var r = await Api.GetAsync<MecanicoDto>($"api/mecanicos/{id}");
        if (!r.Ok || r.Data is null) return ErrorDeCarga(r);
        ViewBag.Id = id;
        return View("Form", r.Data);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id, MecanicoSaveDto model)
    {
        ViewBag.Id = id;
        if (!ModelState.IsValid) return View("Form", model);
        var r = await Api.PutAsync<MecanicoDto>($"api/mecanicos/{id}", model);
        if (!r.Ok) { ErroresAlModelo(r); return View("Form", model); }
        Exito("Mecánico actualizado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await Api.DeleteAsync($"api/mecanicos/{id}");
        if (r.Ok) Exito("Mecánico eliminado."); else Fallo(r);
        return RedirectToAction(nameof(Index));
    }
}
