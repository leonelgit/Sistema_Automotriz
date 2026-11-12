using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Administración de usuarios, roles y permisos (claims). Solo Administrador.</summary>
[Authorize(Roles = Roles.Administrador)]
public class UsuariosController : AppController
{
    public async Task<IActionResult> Index()
    {
        var r = await Api.GetAsync<List<UsuarioDto>>("api/usuarios");
        if (!r.Ok) Fallo(r);
        var m = await Api.GetAsync<List<MecanicoDto>>("api/mecanicos");
        ViewBag.Mecanicos = m.Data ?? new List<MecanicoDto>();
        return View(r.Data ?? new());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(CrearUsuarioRequest model)
    {
        if (model.Permisos is { Count: 0 }) model.Permisos = null; // usar los del rol
        var r = await Api.PostAsync<UsuarioDto>("api/usuarios", model);
        if (r.Ok) Exito($"Usuario {model.Email} creado."); else Fallo(r);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Permisos(int id, List<string>? permisos)
    {
        var r = await Api.PutAsync<UsuarioDto>($"api/usuarios/{id}/permisos", new ActualizarPermisosRequest { Permisos = permisos ?? new() });
        if (r.Ok) Exito("Permisos actualizados. El usuario debe volver a iniciar sesión para que apliquen."); else Fallo(r);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Activo(int id, bool activo)
    {
        var r = await Api.PutAsync<UsuarioDto>($"api/usuarios/{id}/activo?activo={activo}", new { });
        if (r.Ok) Exito(activo ? "Usuario activado." : "Usuario desactivado."); else Fallo(r);
        return RedirectToAction(nameof(Index));
    }
}
