using System.ComponentModel.DataAnnotations;

namespace AutoFix.Contracts;

// ───────────────────────── Errores ─────────────────────────
public class ApiError
{
    public int Status { get; set; }
    public string Error { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public string? CorrelationId { get; set; }
    public List<string> Errores { get; set; } = new();
}

// ───────────────────────── Autenticación ─────────────────────────
public class LoginRequest
{
    [Required(ErrorMessage = "El correo es obligatorio"), EmailAddress(ErrorMessage = "Correo inválido")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "La contraseña es obligatoria")]
    public string Password { get; set; } = "";
}

/// <summary>Registro público: crea un usuario con rol Cliente.</summary>
public class RegistroRequest
{
    [Required, StringLength(120)] public string NombreCompleto { get; set; } = "";
    [Required, StringLength(20)] public string Dui { get; set; } = "";
    [Required, StringLength(20)] public string Telefono { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [StringLength(250)] public string? Direccion { get; set; }
    [Required, MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres")]
    public string Password { get; set; } = "";
}

/// <summary>El Administrador crea usuarios internos (cualquier rol).</summary>
public class CrearUsuarioRequest
{
    [Required, StringLength(120)] public string NombreCompleto { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, MinLength(8)] public string Password { get; set; } = "";
    [Required] public string Rol { get; set; } = Roles.Recepcionista;
    /// <summary>Si es null se usan los permisos por defecto del rol.</summary>
    public List<string>? Permisos { get; set; }
    /// <summary>Para rol Mecánico: vincular a un mecánico existente.</summary>
    public int? MecanicoId { get; set; }
    /// <summary>Para rol Cliente: vincular a un cliente existente.</summary>
    public int? ClienteId { get; set; }
}

public class ActualizarPermisosRequest
{
    public List<string> Permisos { get; set; } = new();
}

public class AuthResponse
{
    public string Token { get; set; } = "";
    public DateTime Expira { get; set; }
    public int UsuarioId { get; set; }
    public string Email { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public List<string> Permisos { get; set; } = new();
    public int? ClienteId { get; set; }
    public int? MecanicoId { get; set; }
}

public class UsuarioDto
{
    public int Id { get; set; }
    public string NombreCompleto { get; set; } = "";
    public string Email { get; set; } = "";
    public string Rol { get; set; } = "";
    public List<string> Permisos { get; set; } = new();
    public bool Activo { get; set; }
    public int? ClienteId { get; set; }
    public int? MecanicoId { get; set; }
}

// ───────────────────────── Clientes y vehículos ─────────────────────────
public class ClienteSaveDto
{
    [Required(ErrorMessage = "El nombre es obligatorio"), StringLength(120)]
    public string NombreCompleto { get; set; } = "";
    [Required(ErrorMessage = "El DUI o identificación es obligatorio"), StringLength(20)]
    public string Dui { get; set; } = "";
    [Required(ErrorMessage = "El teléfono es obligatorio"), StringLength(20)]
    public string Telefono { get; set; } = "";
    [Required(ErrorMessage = "El correo es obligatorio"), EmailAddress(ErrorMessage = "Correo inválido")]
    public string Correo { get; set; } = "";
    [StringLength(250)] public string? Direccion { get; set; }
}

public class ClienteDto : ClienteSaveDto
{
    public int Id { get; set; }
    public DateTime FechaRegistro { get; set; }
    public int CantidadVehiculos { get; set; }
    public bool TieneUsuario { get; set; }
    public List<VehiculoDto> Vehiculos { get; set; } = new();
}

public class VehiculoSaveDto
{
    [Required(ErrorMessage = "La placa es obligatoria"), StringLength(15)]
    public string Placa { get; set; } = "";
    [Required, StringLength(50)] public string Marca { get; set; } = "";
    [Required, StringLength(50)] public string Modelo { get; set; } = "";
    [Range(1950, 2100, ErrorMessage = "Año inválido")] public int Anio { get; set; } = DateTime.Today.Year;
    [Required, StringLength(30)] public string Color { get; set; } = "";
    [Required(ErrorMessage = "El número de chasis es obligatorio"), StringLength(30)]
    public string NumeroChasis { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione el cliente propietario")]
    public int ClienteId { get; set; }
}

public class VehiculoDto : VehiculoSaveDto
{
    public int Id { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string? FotoRuta { get; set; }
    public string Descripcion => $"{Placa} · {Marca} {Modelo} {Anio}";
}

// ───────────────────────── Mecánicos y asignaciones ─────────────────────────
public class MecanicoSaveDto
{
    [Required, StringLength(120)] public string Nombre { get; set; } = "";
    [StringLength(80)] public string? Especialidad { get; set; }
    [StringLength(20)] public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Token de concurrencia que el cliente leyó (para edición).</summary>
    public Guid? Version { get; set; }
}

public class MecanicoDto : MecanicoSaveDto
{
    public int Id { get; set; }
    public string? UsuarioEmail { get; set; }
    public int OrdenesActivas { get; set; }
    public DateTime? UltimaAsignacion { get; set; }
}

public class AsignarMecanicoRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione un mecánico")]
    public int MecanicoId { get; set; }
    [Required] public DateTime Inicio { get; set; }
    [Required] public DateTime Fin { get; set; }
    /// <summary>Versión del mecánico que leyó el supervisor (opcional). Si cambió => 409.</summary>
    public Guid? MecanicoVersion { get; set; }
}

public class AsignacionDto
{
    public int Id { get; set; }
    public int MecanicoId { get; set; }
    public string MecanicoNombre { get; set; } = "";
    public int OrdenId { get; set; }
    public string NumeroOrden { get; set; } = "";
    public EstadoOrden EstadoOrden { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public bool Activa { get; set; }
    public string AsignadoPor { get; set; } = "";
    public DateTime FechaAsignacion { get; set; }
}

// ───────────────────────── Órdenes ─────────────────────────
public class CrearOrdenRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione el vehículo")]
    public int VehiculoId { get; set; }
    [Required(ErrorMessage = "Indique el servicio solicitado"), StringLength(100)]
    public string TipoServicio { get; set; } = "";
    [Required] public DateTime FechaEstimadaFin { get; set; } = DateTime.Today.AddDays(3).AddHours(17);
    [Required(ErrorMessage = "Describa el problema"), StringLength(1000)]
    public string DescripcionProblema { get; set; } = "";
}

public class EditarOrdenRequest
{
    [Required, StringLength(1000)] public string DescripcionProblema { get; set; } = "";
    [StringLength(2000)] public string? Diagnostico { get; set; }
    [Required] public DateTime FechaEstimadaFin { get; set; }
    /// <summary>Token de concurrencia leído por el usuario.</summary>
    public Guid Version { get; set; }
}

public class CambiarEstadoRequest
{
    public EstadoOrden NuevoEstado { get; set; }
    [StringLength(500)] public string? Comentario { get; set; }
    public Guid? Version { get; set; }
}

public class ComentarioRequest
{
    [StringLength(500)] public string? Comentario { get; set; }
}

public class HistorialEstadoDto
{
    public EstadoOrden? EstadoAnterior { get; set; }
    public EstadoOrden EstadoNuevo { get; set; }
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string? Comentario { get; set; }
}

public class OrdenDto
{
    public int Id { get; set; }
    public string NumeroOrden { get; set; } = "";
    public int ClienteId { get; set; }
    public string ClienteNombre { get; set; } = "";
    public int VehiculoId { get; set; }
    public string VehiculoDescripcion { get; set; } = "";
    public string TipoServicio { get; set; } = "";
    public DateTime FechaIngreso { get; set; }
    public DateTime FechaEstimadaFin { get; set; }
    public DateTime? FechaFinalizacion { get; set; }
    public string DescripcionProblema { get; set; } = "";
    public string? Diagnostico { get; set; }
    public int? MecanicoId { get; set; }
    public string? MecanicoNombre { get; set; }
    public EstadoOrden Estado { get; set; }
    public Guid Version { get; set; }
    public string CreadoPor { get; set; } = "";
    public EstadoPresupuesto? EstadoPresupuesto { get; set; }
    public decimal? TotalPresupuesto { get; set; }
}

public class OrdenDetalleDto : OrdenDto
{
    public List<HistorialEstadoDto> Historial { get; set; } = new();
    public List<EvidenciaDto> Evidencias { get; set; } = new();
    public List<AsignacionDto> Asignaciones { get; set; } = new();
    public PresupuestoDto? Presupuesto { get; set; }
}

// ───────────────────────── Presupuestos ─────────────────────────
public class LineaPresupuestoRequest
{
    [Range(1, int.MaxValue)] public int RepuestoId { get; set; }
    [Range(1, 10000, ErrorMessage = "Cantidad inválida")] public int Cantidad { get; set; } = 1;
    /// <summary>Si es null se usa el precio actual del repuesto.</summary>
    [Range(0, 1_000_000)] public decimal? PrecioUnitario { get; set; }
}

public class CrearPresupuestoRequest
{
    [Range(0, 1_000_000, ErrorMessage = "Mano de obra inválida")] public decimal ManoObra { get; set; }
    [StringLength(1000)] public string? Observaciones { get; set; }
    public List<LineaPresupuestoRequest> Repuestos { get; set; } = new();
}

public class DetallePresupuestoDto
{
    public int RepuestoId { get; set; }
    public string RepuestoCodigo { get; set; } = "";
    public string RepuestoNombre { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public int ExistenciasActuales { get; set; }
}

public class PresupuestoDto
{
    public int Id { get; set; }
    public int OrdenId { get; set; }
    public string NumeroOrden { get; set; } = "";
    public string ClienteNombre { get; set; } = "";
    public string VehiculoDescripcion { get; set; } = "";
    public EstadoOrden EstadoOrden { get; set; }
    public decimal ManoObra { get; set; }
    public decimal TotalRepuestos { get; set; }
    public decimal Total { get; set; }
    public string? Observaciones { get; set; }
    public EstadoPresupuesto Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public string CreadoPor { get; set; } = "";
    public string? ResueltoPor { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public string? ComentarioResolucion { get; set; }
    public bool InventarioDescontado { get; set; }
    public List<DetallePresupuestoDto> Detalles { get; set; } = new();
}

// ───────────────────────── Inventario ─────────────────────────
public class RepuestoSaveDto
{
    [Required(ErrorMessage = "El código es obligatorio"), StringLength(30)] public string Codigo { get; set; } = "";
    [Required(ErrorMessage = "El nombre es obligatorio"), StringLength(120)] public string Nombre { get; set; } = "";
    [StringLength(500)] public string? Descripcion { get; set; }
    [Required, StringLength(60)] public string Categoria { get; set; } = "";
    [Required, StringLength(60)] public string Marca { get; set; } = "";
    [Range(0, 1_000_000, ErrorMessage = "Precio inválido")] public decimal Precio { get; set; }
    /// <summary>Solo se usa al crear. Después cambia únicamente con entradas/salidas.</summary>
    [Range(0, 1_000_000)] public int Existencias { get; set; }
    [Range(0, 1_000_000)] public int StockMinimo { get; set; }
    public Guid? Version { get; set; }
}

public class RepuestoDto : RepuestoSaveDto
{
    public int Id { get; set; }
    public string? FotoRuta { get; set; }
    public bool BajoStock => Existencias <= StockMinimo;
}

public class MovimientoRequest
{
    [Range(1, 1_000_000, ErrorMessage = "La cantidad debe ser mayor que cero")] public int Cantidad { get; set; } = 1;
    [StringLength(250)] public string? Motivo { get; set; }
    public int? OrdenId { get; set; }
    /// <summary>Versión del repuesto que vio el usuario. Si otro la cambió antes => 409.</summary>
    public Guid? Version { get; set; }
}

public class MovimientoDto
{
    public int Id { get; set; }
    public int RepuestoId { get; set; }
    public string RepuestoCodigo { get; set; } = "";
    public string RepuestoNombre { get; set; } = "";
    public TipoMovimiento Tipo { get; set; }
    public int Cantidad { get; set; }
    public int ExistenciaAnterior { get; set; }
    public int ExistenciaResultante { get; set; }
    public string? Motivo { get; set; }
    public int? OrdenId { get; set; }
    public string? NumeroOrden { get; set; }
    public string Usuario { get; set; } = "";
    public DateTime Fecha { get; set; }
}

public class MovimientoResultadoDto
{
    public MovimientoDto Movimiento { get; set; } = new();
    public int ExistenciasActuales { get; set; }
    public bool AlertaStockMinimo { get; set; }
    public string Mensaje { get; set; } = "";
}

// ───────────────────────── Evidencias ─────────────────────────
public class EvidenciaDto
{
    public int Id { get; set; }
    public int OrdenId { get; set; }
    public string NumeroOrden { get; set; } = "";
    public TipoEvidencia Tipo { get; set; }
    public string Ruta { get; set; } = "";
    public string? Descripcion { get; set; }
    public string NombreOriginal { get; set; } = "";
    public long TamanoBytes { get; set; }
    public DateTime Fecha { get; set; }
    public string SubidoPor { get; set; } = "";
}

public class ImagenSubidaDto
{
    public string Ruta { get; set; } = "";
}

// ───────────────────────── Reportes y logs ─────────────────────────
public class DashboardDto
{
    public int TotalClientes { get; set; }
    public int TotalVehiculos { get; set; }
    public int OrdenesActivas { get; set; }
    public int OrdenesFinalizadasMes { get; set; }
    public int PresupuestosPendientes { get; set; }
    public int MecanicosActivos { get; set; }
    public decimal IngresosAprobadosMes { get; set; }
    public Dictionary<string, int> OrdenesPorEstado { get; set; } = new();
    public List<RepuestoDto> RepuestosBajoStock { get; set; } = new();
    public List<OrdenDto> OrdenesRecientes { get; set; } = new();
}

public class ReporteOrdenesDto
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public int Total { get; set; }
    public decimal MontoPresupuestosAprobados { get; set; }
    public Dictionary<string, int> PorEstado { get; set; } = new();
    public List<OrdenDto> Ordenes { get; set; } = new();
}

public class ReporteMecanicoDto
{
    public int MecanicoId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; }
    public int OrdenesActivas { get; set; }
    public int OrdenesFinalizadas { get; set; }
    public int AsignacionesFuturas { get; set; }
}

public class ReporteInventarioDto
{
    public int TotalRepuestos { get; set; }
    public int UnidadesTotales { get; set; }
    public decimal ValorInventario { get; set; }
    public List<RepuestoDto> BajoStock { get; set; } = new();
    public List<MovimientoDto> UltimosMovimientos { get; set; } = new();
}

public class LogDto
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Usuario { get; set; } = "";
    public string Metodo { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public int Codigo { get; set; }
    public long DuracionMs { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? Ip { get; set; }
    public string? Excepcion { get; set; }
    public string? Detalle { get; set; }
}
