using System.Globalization;
using System.Security.Claims;
using AutoFix.Contracts;

namespace AutoFix.Mvc.Services;

/// <summary>Ayudas de presentación para las vistas.</summary>
public static class Ui
{
    public static string ApiBase { get; set; } = "";
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    /// <summary>URL pública de una imagen guardada por la API (la API sirve /uploads como archivos estáticos).</summary>
    public static string Img(string? ruta) =>
        string.IsNullOrWhiteSpace(ruta) ? "" : ApiBase.TrimEnd('/') + "/" + ruta.TrimStart('/');

    public static string Dinero(decimal? valor) => (valor ?? 0).ToString("C2", Usd);
    public static string Fecha(DateTime? f) => f?.ToString("dd/MM/yyyy HH:mm") ?? "—";
    public static string FechaCorta(DateTime? f) => f?.ToString("dd/MM/yyyy") ?? "—";
    public static string ParaInput(DateTime f) => f.ToString("yyyy-MM-ddTHH:mm");

    public static string ClaseEstado(EstadoOrden e) => e switch
    {
        EstadoOrden.Recibido => "estado-recibido",
        EstadoOrden.EnDiagnostico => "estado-diagnostico",
        EstadoOrden.Presupuestado => "estado-presupuestado",
        EstadoOrden.PendienteAprobacion => "estado-pendiente",
        EstadoOrden.Aprobado => "estado-aprobado",
        EstadoOrden.EnReparacion => "estado-reparacion",
        EstadoOrden.Finalizado => "estado-finalizado",
        EstadoOrden.Entregado => "estado-entregado",
        EstadoOrden.Cancelado => "estado-cancelado",
        _ => ""
    };

    public static string ClasePresupuesto(EstadoPresupuesto? e) => e switch
    {
        EstadoPresupuesto.Aprobado => "text-bg-success",
        EstadoPresupuesto.Rechazado => "text-bg-danger",
        EstadoPresupuesto.Pendiente => "text-bg-warning",
        _ => "text-bg-secondary"
    };

    public static string TextoPresupuesto(EstadoPresupuesto? e) => e switch
    {
        EstadoPresupuesto.Aprobado => "Aprobado",
        EstadoPresupuesto.Rechazado => "Rechazado",
        EstadoPresupuesto.Pendiente => "Pendiente",
        _ => "Sin presupuesto"
    };

    public static string ClaseHttp(int codigo) => codigo switch
    {
        >= 500 => "text-bg-danger",
        >= 400 => "text-bg-warning",
        _ => "text-bg-success"
    };

    /// <summary>Pasos del flujo normal (para la línea de progreso).</summary>
    public static readonly EstadoOrden[] Flujo =
    {
        EstadoOrden.Recibido, EstadoOrden.EnDiagnostico, EstadoOrden.Presupuestado, EstadoOrden.PendienteAprobacion,
        EstadoOrden.Aprobado, EstadoOrden.EnReparacion, EstadoOrden.Finalizado, EstadoOrden.Entregado
    };
}

/// <summary>Datos del usuario guardados en la cookie del MVC.</summary>
public static class UserExtensions
{
    public const string TokenClaim = "access_token";

    public static string Nombre(this ClaimsPrincipal u) => u.FindFirst(ClaimTypes.Name)?.Value ?? "";
    public static string Rol(this ClaimsPrincipal u) => u.FindFirst(ClaimTypes.Role)?.Value ?? "";
    public static bool TienePermiso(this ClaimsPrincipal u, string permiso) => u.HasClaim(Permisos.ClaimType, permiso);
    public static bool EsEmpleado(this ClaimsPrincipal u) => u.Identity?.IsAuthenticated == true && !u.IsInRole(Roles.Cliente);
    public static bool EsAlguno(this ClaimsPrincipal u, params string[] roles) => roles.Any(u.IsInRole);
    public static int? MecanicoId(this ClaimsPrincipal u) => int.TryParse(u.FindFirst("mecanicoId")?.Value, out var id) ? id : null;
}
