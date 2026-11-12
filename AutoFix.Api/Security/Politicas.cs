using System.Security.Claims;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace AutoFix.Api.Security;

/// <summary>
/// Políticas que combinan ROL (el puesto) + CLAIM (el permiso concreto).
/// Ejemplo: "PuedeAprobar" = rol Supervisor/Administrador Y claim permiso=Ordenes.Aprobar.
/// Un Supervisor sin el claim recibe 403 aunque tenga el rol.
/// </summary>
public static class Politicas
{
    public const string Empleados = "Empleados";
    public const string PuedeCrearOrdenes = "PuedeCrearOrdenes";
    public const string PuedeEditarOrdenes = "PuedeEditarOrdenes";
    public const string PuedeAsignar = "PuedeAsignar";
    public const string PuedeAprobar = "PuedeAprobar";
    public const string PuedeFinalizar = "PuedeFinalizar";
    public const string PuedeCancelar = "PuedeCancelar";
    public const string PuedeGestionarInventario = "PuedeGestionarInventario";
    public const string PuedeUsarRepuestos = "PuedeUsarRepuestos";
    public const string PuedeConsultarReportes = "PuedeConsultarReportes";

    public static void Registrar(AuthorizationOptions o)
    {
        // Cualquier endpoint sin [AllowAnonymous] exige JWT válido => 401 si no hay token.
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        o.AddPolicy(Empleados, p => p.RequireRole(Roles.Administrador, Roles.Recepcionista, Roles.Mecanico, Roles.Supervisor));

        Agregar(o, PuedeCrearOrdenes, Permisos.OrdenesCrear, Roles.Administrador, Roles.Recepcionista);
        Agregar(o, PuedeEditarOrdenes, Permisos.OrdenesEditar, Roles.Administrador, Roles.Recepcionista, Roles.Mecanico, Roles.Supervisor);
        Agregar(o, PuedeAsignar, Permisos.OrdenesAsignar, Roles.Administrador, Roles.Supervisor);
        Agregar(o, PuedeAprobar, Permisos.OrdenesAprobar, Roles.Administrador, Roles.Supervisor);
        Agregar(o, PuedeFinalizar, Permisos.OrdenesFinalizar, Roles.Administrador, Roles.Supervisor);
        Agregar(o, PuedeCancelar, Permisos.OrdenesEditar, Roles.Administrador, Roles.Supervisor);
        Agregar(o, PuedeGestionarInventario, Permisos.InventarioGestionar, Roles.Administrador, Roles.Supervisor);
        Agregar(o, PuedeUsarRepuestos, Permisos.OrdenesEditar, Roles.Administrador, Roles.Supervisor, Roles.Mecanico);
        Agregar(o, PuedeConsultarReportes, Permisos.ReportesConsultar, Roles.Administrador, Roles.Supervisor);
    }

    private static void Agregar(AuthorizationOptions o, string nombre, string permiso, params string[] roles) =>
        o.AddPolicy(nombre, p => p.RequireAuthenticatedUser()
                                  .RequireRole(roles)
                                  .RequireClaim(Permisos.ClaimType, permiso));
}

/// <summary>Atajos para leer los datos del usuario autenticado desde el JWT.</summary>
public static class ClaimsPrincipalExtensions
{
    public static string Email(this ClaimsPrincipal u) => u.FindFirst("email")?.Value ?? "anónimo";
    public static int UsuarioId(this ClaimsPrincipal u) => int.TryParse(u.FindFirst("sub")?.Value, out var id) ? id : 0;
    public static int? ClienteId(this ClaimsPrincipal u) => int.TryParse(u.FindFirst("clienteId")?.Value, out var id) ? id : null;
    public static int? MecanicoId(this ClaimsPrincipal u) => int.TryParse(u.FindFirst("mecanicoId")?.Value, out var id) ? id : null;
    public static bool EsCliente(this ClaimsPrincipal u) => u.IsInRole(Roles.Cliente);
    public static bool EsMecanico(this ClaimsPrincipal u) => u.IsInRole(Roles.Mecanico);
    public static bool TienePermiso(this ClaimsPrincipal u, string permiso) => u.HasClaim(Permisos.ClaimType, permiso);
}
