using AutoFix.Mvc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Base de los controladores MVC: acceso a la API y mensajes para el usuario.</summary>
[Authorize]
public abstract class AppController : Controller
{
    protected ApiClient Api => HttpContext.RequestServices.GetRequiredService<ApiClient>();

    protected void Exito(string mensaje) => TempData["Ok"] = mensaje;
    protected void Fallo(string mensaje) => TempData["Error"] = mensaje;
    protected void Fallo<T>(ApiResult<T> r) => TempData["Error"] = r.MensajeCompleto;
    protected void Alerta(string mensaje) => TempData["Alerta"] = mensaje;

    /// <summary>Pasa los errores de la API al formulario.</summary>
    protected void ErroresAlModelo<T>(ApiResult<T> r)
    {
        ModelState.AddModelError(string.Empty, r.MensajeCompleto);
    }

    /// <summary>Muestra un error de la API cuando se intenta CARGAR una página (403, 404...).</summary>
    protected IActionResult ErrorDeCarga<T>(ApiResult<T> r, string accion = "Index")
    {
        Fallo(r);
        return r.Status == 403 ? RedirectToAction("AccesoDenegado", "Account") : RedirectToAction(accion);
    }

    /// <summary>Regresa a la página anterior (solo URLs locales).</summary>
    protected IActionResult Volver(string accion = "Index", object? valores = null)
    {
        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && uri.Host == Request.Host.Host && Url.IsLocalUrl(uri.PathAndQuery))
            return LocalRedirect(uri.PathAndQuery);
        return RedirectToAction(accion, valores);
    }
}
