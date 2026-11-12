namespace AutoFix.Contracts;

/// <summary>Estados de una orden de reparación (módulo 3).</summary>
public enum EstadoOrden
{
    Recibido = 1,
    EnDiagnostico = 2,
    Presupuestado = 3,
    PendienteAprobacion = 4,
    Aprobado = 5,
    EnReparacion = 6,
    Finalizado = 7,
    Entregado = 8,
    Cancelado = 9
}

public enum EstadoPresupuesto { Pendiente = 1, Aprobado = 2, Rechazado = 3 }

public enum TipoMovimiento { Entrada = 1, Salida = 2 }

/// <summary>Tipos de fotografía (módulo 7).</summary>
public enum TipoEvidencia { Ingreso = 1, Danio = 2, Repuesto = 3, Finalizacion = 4 }

/// <summary>Roles del sistema.</summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Recepcionista = "Recepcionista";
    public const string Mecanico = "Mecanico";
    public const string Supervisor = "Supervisor";
    public const string Cliente = "Cliente";

    /// <summary>Todos los roles internos del taller (para [Authorize(Roles = ...)]).</summary>
    public const string Empleados = "Administrador,Recepcionista,Mecanico,Supervisor";

    public static readonly string[] Todos = { Administrador, Recepcionista, Mecanico, Supervisor, Cliente };
}

/// <summary>Claims (permisos) obligatorios del módulo 1. Tipo de claim: "permiso".</summary>
public static class Permisos
{
    public const string ClaimType = "permiso";

    public const string OrdenesCrear = "Ordenes.Crear";
    public const string OrdenesEditar = "Ordenes.Editar";
    public const string OrdenesAsignar = "Ordenes.Asignar";
    public const string OrdenesAprobar = "Ordenes.Aprobar";
    public const string OrdenesFinalizar = "Ordenes.Finalizar";
    public const string InventarioGestionar = "Inventario.Gestionar";
    public const string ReportesConsultar = "Reportes.Consultar";

    public static readonly string[] Todos =
    {
        OrdenesCrear, OrdenesEditar, OrdenesAsignar, OrdenesAprobar,
        OrdenesFinalizar, InventarioGestionar, ReportesConsultar
    };

    /// <summary>Permisos que recibe por defecto cada rol al crearse un usuario.</summary>
    public static string[] PorDefecto(string rol) => rol switch
    {
        Roles.Administrador => Todos,
        Roles.Recepcionista => new[] { OrdenesCrear, OrdenesEditar },
        Roles.Mecanico => new[] { OrdenesEditar },
        Roles.Supervisor => new[] { OrdenesEditar, OrdenesAsignar, OrdenesAprobar, OrdenesFinalizar, InventarioGestionar, ReportesConsultar },
        _ => Array.Empty<string>()
    };
}

public static class EstadoOrdenTexto
{
    public static string Texto(this EstadoOrden e) => e switch
    {
        EstadoOrden.Recibido => "Recibido",
        EstadoOrden.EnDiagnostico => "En diagnóstico",
        EstadoOrden.Presupuestado => "Presupuestado",
        EstadoOrden.PendienteAprobacion => "Pendiente de aprobación",
        EstadoOrden.Aprobado => "Aprobado",
        EstadoOrden.EnReparacion => "En reparación",
        EstadoOrden.Finalizado => "Finalizado",
        EstadoOrden.Entregado => "Entregado",
        EstadoOrden.Cancelado => "Cancelado",
        _ => e.ToString()
    };

    public static string Texto(this TipoEvidencia t) => t switch
    {
        TipoEvidencia.Ingreso => "Vehículo al ingresar",
        TipoEvidencia.Danio => "Daño identificado",
        TipoEvidencia.Repuesto => "Repuesto utilizado",
        TipoEvidencia.Finalizacion => "Reparación finalizada",
        _ => t.ToString()
    };

    /// <summary>Estados en los que la orden se considera "activa" (sirve para detectar duplicados).</summary>
    public static bool EsActiva(this EstadoOrden e) => e != EstadoOrden.Entregado && e != EstadoOrden.Cancelado;
}
