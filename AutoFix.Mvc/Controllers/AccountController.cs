using System.Security.Claims;
using AutoFix.Contracts;
using AutoFix.Mvc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoFix.Mvc.Controllers;

/// <summary>Vista Login (y registro de clientes). El MVC pide el JWT a la API y lo guarda en la cookie.</summary>
[AllowAnonymous]
public class AccountController : AppController
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginRequest());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var r = await Api.PostAsync<AuthResponse>("api/auth/login", model);
        if (!r.Ok || r.Data is null)
        {
            ModelState.AddModelError(string.Empty, r.Mensaje);
            return View(model);
        }

        await IniciarSesionAsync(r.Data);
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Registro() => View(new RegistroRequest());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Registro(RegistroRequest model)
    {
        if (!ModelState.IsValid) return View(model);
        var r = await Api.PostAsync<AuthResponse>("api/auth/registro", model);
        if (!r.Ok || r.Data is null)
        {
            ErroresAlModelo(r);
            return View(model);
        }
        await IniciarSesionAsync(r.Data);
        Exito("¡Bienvenido! Su cuenta de cliente fue creada.");
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccesoDenegado() => View();

    private async Task IniciarSesionAsync(AuthResponse a)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, a.Nombre),
            new(ClaimTypes.Email, a.Email),
            new(ClaimTypes.Role, a.Rol),
            new("usuarioId", a.UsuarioId.ToString()),
            new(UserExtensions.TokenClaim, a.Token)
        };
        claims.AddRange(a.Permisos.Select(p => new Claim(Permisos.ClaimType, p)));
        if (a.ClienteId.HasValue) claims.Add(new Claim("clienteId", a.ClienteId.Value.ToString()));
        if (a.MecanicoId.HasValue) claims.Add(new Claim("mecanicoId", a.MecanicoId.Value.ToString()));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(a.Expira, DateTimeKind.Utc))
            });
    }
}
