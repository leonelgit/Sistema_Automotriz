using System.ComponentModel.DataAnnotations;
using AutoFix.Contracts;

namespace AutoFix.Api.Entities;

/// <summary>
/// Token de concurrencia para SQLite. SQLite no genera RowVersion automáticamente,
/// así que usamos un Guid que se regenera en cada modificación (ver AutoFixDbContext.SaveChanges).
/// EF Core agrega "WHERE Version = @original" al UPDATE; si no afecta filas lanza DbUpdateConcurrencyException.
/// </summary>
public abstract class EntidadConcurrente
{
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class Usuario
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Rol { get; set; } = Roles.Cliente;
    /// <summary>Permisos (claims) separados por coma.</summary>
    public string Permisos { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public int? MecanicoId { get; set; }
    public Mecanico? Mecanico { get; set; }

    public List<string> ListaPermisos() =>
        Permisos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public class Cliente
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Dui { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Correo { get; set; } = "";
    public string? Direccion { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.Now;
    public List<Vehiculo> Vehiculos { get; set; } = new();
    public List<OrdenReparacion> Ordenes { get; set; } = new();
}

public class Vehiculo
{
    public int Id { get; set; }
    public string Placa { get; set; } = "";
    public string Marca { get; set; } = "";
    public string Modelo { get; set; } = "";
    public int Anio { get; set; }
    public string Color { get; set; } = "";
    public string NumeroChasis { get; set; } = "";
    public string? FotoRuta { get; set; }
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;
}

public class Mecanico : EntidadConcurrente
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Especialidad { get; set; }
    public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Se modifica en cada asignación para "tocar" la fila y cambiar su token Version (escenario B).</summary>
    public DateTime? UltimaAsignacion { get; set; }
    public List<AsignacionMecanico> Asignaciones { get; set; } = new();
}

public class AsignacionMecanico
{
    public int Id { get; set; }
    public int MecanicoId { get; set; }
    public Mecanico Mecanico { get; set; } = null!;
    public int OrdenId { get; set; }
    public OrdenReparacion Orden { get; set; } = null!;
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public bool Activa { get; set; } = true;
    public string AsignadoPor { get; set; } = "";
    public DateTime FechaAsignacion { get; set; } = DateTime.Now;
}

public class OrdenReparacion : EntidadConcurrente
{
    public int Id { get; set; }
    public string NumeroOrden { get; set; } = "";
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;
    public int VehiculoId { get; set; }
    public Vehiculo Vehiculo { get; set; } = null!;
    public string TipoServicio { get; set; } = "";
    public DateTime FechaIngreso { get; set; } = DateTime.Now;
    public DateTime FechaEstimadaFin { get; set; }
    public DateTime? FechaFinalizacion { get; set; }
    public string DescripcionProblema { get; set; } = "";
    public string? Diagnostico { get; set; }
    public int? MecanicoId { get; set; }
    public Mecanico? Mecanico { get; set; }
    public EstadoOrden Estado { get; set; } = EstadoOrden.Recibido;
    public string CreadoPor { get; set; } = "";

    public Presupuesto? Presupuesto { get; set; }
    public List<Evidencia> Evidencias { get; set; } = new();
    public List<HistorialEstado> Historial { get; set; } = new();
    public List<AsignacionMecanico> Asignaciones { get; set; } = new();
}

/// <summary>Auditoría de cambios de estado: quién, cuándo y por qué.</summary>
public class HistorialEstado
{
    public int Id { get; set; }
    public int OrdenId { get; set; }
    public OrdenReparacion Orden { get; set; } = null!;
    public EstadoOrden? EstadoAnterior { get; set; }
    public EstadoOrden EstadoNuevo { get; set; }
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.Now;
    public string? Comentario { get; set; }
}

public class Presupuesto
{
    public int Id { get; set; }
    public int OrdenId { get; set; }
    public OrdenReparacion Orden { get; set; } = null!;
    public decimal ManoObra { get; set; }
    public decimal Total { get; set; }
    public string? Observaciones { get; set; }
    public EstadoPresupuesto Estado { get; set; } = EstadoPresupuesto.Pendiente;
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
    public string CreadoPor { get; set; } = "";
    public string? ResueltoPor { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public string? ComentarioResolucion { get; set; }
    /// <summary>Evita descontar dos veces los repuestos del inventario.</summary>
    public bool InventarioDescontado { get; set; }
    public List<DetallePresupuesto> Detalles { get; set; } = new();
}

public class DetallePresupuesto
{
    public int Id { get; set; }
    public int PresupuestoId { get; set; }
    public Presupuesto Presupuesto { get; set; } = null!;
    public int RepuestoId { get; set; }
    public Repuesto Repuesto { get; set; } = null!;
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
}

public class Repuesto : EntidadConcurrente
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public string Categoria { get; set; } = "";
    public string Marca { get; set; } = "";
    public decimal Precio { get; set; }
    public int Existencias { get; set; }
    public int StockMinimo { get; set; }
    public string? FotoRuta { get; set; }
    public bool Activo { get; set; } = true;
}

public class MovimientoInventario
{
    public int Id { get; set; }
    public int RepuestoId { get; set; }
    public Repuesto Repuesto { get; set; } = null!;
    public TipoMovimiento Tipo { get; set; }
    public int Cantidad { get; set; }
    public int ExistenciaAnterior { get; set; }
    public int ExistenciaResultante { get; set; }
    public string? Motivo { get; set; }
    public int? OrdenId { get; set; }
    public OrdenReparacion? Orden { get; set; }
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; } = DateTime.Now;
}

/// <summary>En la base solo se guarda la RUTA de la imagen, nunca la imagen.</summary>
public class Evidencia
{
    public int Id { get; set; }
    public int OrdenId { get; set; }
    public OrdenReparacion Orden { get; set; } = null!;
    public TipoEvidencia Tipo { get; set; }
    public string Ruta { get; set; } = "";
    public string NombreOriginal { get; set; } = "";
    public long TamanoBytes { get; set; }
    public string? Descripcion { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Now;
    public string SubidoPor { get; set; } = "";
}

/// <summary>Log de cada solicitud HTTP (módulo 8).</summary>
public class LogSolicitud
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Now;
    public string Usuario { get; set; } = "anónimo";
    public string Metodo { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public int Codigo { get; set; }
    public long DuracionMs { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? Ip { get; set; }
    public string? Excepcion { get; set; }
    public string? Detalle { get; set; }
}
