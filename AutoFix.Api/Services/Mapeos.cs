using AutoFix.Api.Entities;
using AutoFix.Contracts;

namespace AutoFix.Api.Services;

/// <summary>Conversión de entidades a DTOs (lo que viaja por HTTP). Nunca se exponen las entidades.</summary>
public static class Mapeos
{
    public static UsuarioDto ToDto(this Usuario u) => new()
    {
        Id = u.Id, NombreCompleto = u.NombreCompleto, Email = u.Email, Rol = u.Rol,
        Permisos = u.ListaPermisos(), Activo = u.Activo, ClienteId = u.ClienteId, MecanicoId = u.MecanicoId
    };

    public static ClienteDto ToDto(this Cliente c, bool tieneUsuario = false) => new()
    {
        Id = c.Id, NombreCompleto = c.NombreCompleto, Dui = c.Dui, Telefono = c.Telefono, Correo = c.Correo,
        Direccion = c.Direccion, FechaRegistro = c.FechaRegistro, CantidadVehiculos = c.Vehiculos.Count,
        TieneUsuario = tieneUsuario,
        Vehiculos = c.Vehiculos.OrderBy(v => v.Placa).Select(v => v.ToDto(c.NombreCompleto)).ToList()
    };

    public static VehiculoDto ToDto(this Vehiculo v, string? clienteNombre = null) => new()
    {
        Id = v.Id, Placa = v.Placa, Marca = v.Marca, Modelo = v.Modelo, Anio = v.Anio, Color = v.Color,
        NumeroChasis = v.NumeroChasis, ClienteId = v.ClienteId, FotoRuta = v.FotoRuta,
        ClienteNombre = clienteNombre ?? v.Cliente?.NombreCompleto ?? ""
    };

    public static string Descripcion(this Vehiculo? v) =>
        v is null ? "" : $"{v.Placa} · {v.Marca} {v.Modelo} {v.Anio}";

    public static MecanicoDto ToDto(this Mecanico m, int ordenesActivas = 0, string? email = null) => new()
    {
        Id = m.Id, Nombre = m.Nombre, Especialidad = m.Especialidad, Telefono = m.Telefono, Activo = m.Activo,
        Version = m.Version, OrdenesActivas = ordenesActivas, UsuarioEmail = email, UltimaAsignacion = m.UltimaAsignacion
    };

    public static AsignacionDto ToDto(this AsignacionMecanico a) => new()
    {
        Id = a.Id, MecanicoId = a.MecanicoId, MecanicoNombre = a.Mecanico?.Nombre ?? "",
        OrdenId = a.OrdenId, NumeroOrden = a.Orden?.NumeroOrden ?? "", EstadoOrden = a.Orden?.Estado ?? default,
        Inicio = a.Inicio, Fin = a.Fin, Activa = a.Activa, AsignadoPor = a.AsignadoPor, FechaAsignacion = a.FechaAsignacion
    };

    public static OrdenDto ToDto(this OrdenReparacion o) => Llenar(new OrdenDto(), o);

    public static OrdenDetalleDto ToDetalleDto(this OrdenReparacion o)
    {
        var d = Llenar(new OrdenDetalleDto(), o);
        d.Historial = o.Historial.OrderBy(h => h.Fecha).Select(h => new HistorialEstadoDto
        {
            EstadoAnterior = h.EstadoAnterior, EstadoNuevo = h.EstadoNuevo, Usuario = h.Usuario,
            Fecha = h.Fecha, Comentario = h.Comentario
        }).ToList();
        d.Evidencias = o.Evidencias.OrderByDescending(e => e.Fecha).Select(e => e.ToDto(o.NumeroOrden)).ToList();
        d.Asignaciones = o.Asignaciones.OrderByDescending(a => a.FechaAsignacion).Select(a => a.ToDto()).ToList();
        d.Presupuesto = o.Presupuesto?.ToDto(o);
        return d;
    }

    private static T Llenar<T>(T d, OrdenReparacion o) where T : OrdenDto
    {
        d.Id = o.Id; d.NumeroOrden = o.NumeroOrden; d.ClienteId = o.ClienteId;
        d.ClienteNombre = o.Cliente?.NombreCompleto ?? ""; d.VehiculoId = o.VehiculoId;
        d.VehiculoDescripcion = o.Vehiculo.Descripcion(); d.TipoServicio = o.TipoServicio;
        d.FechaIngreso = o.FechaIngreso; d.FechaEstimadaFin = o.FechaEstimadaFin; d.FechaFinalizacion = o.FechaFinalizacion;
        d.DescripcionProblema = o.DescripcionProblema; d.Diagnostico = o.Diagnostico;
        d.MecanicoId = o.MecanicoId; d.MecanicoNombre = o.Mecanico?.Nombre; d.Estado = o.Estado;
        d.Version = o.Version; d.CreadoPor = o.CreadoPor;
        d.EstadoPresupuesto = o.Presupuesto?.Estado; d.TotalPresupuesto = o.Presupuesto?.Total;
        return d;
    }

    public static PresupuestoDto ToDto(this Presupuesto p, OrdenReparacion? orden = null)
    {
        orden ??= p.Orden;
        var detalles = p.Detalles.Select(d => new DetallePresupuestoDto
        {
            RepuestoId = d.RepuestoId, RepuestoCodigo = d.Repuesto?.Codigo ?? "", RepuestoNombre = d.Repuesto?.Nombre ?? "",
            Cantidad = d.Cantidad, PrecioUnitario = d.PrecioUnitario, Subtotal = d.Subtotal,
            ExistenciasActuales = d.Repuesto?.Existencias ?? 0
        }).ToList();

        return new PresupuestoDto
        {
            Id = p.Id, OrdenId = p.OrdenId, NumeroOrden = orden?.NumeroOrden ?? "",
            ClienteNombre = orden?.Cliente?.NombreCompleto ?? "", VehiculoDescripcion = orden?.Vehiculo.Descripcion() ?? "",
            EstadoOrden = orden?.Estado ?? default,
            ManoObra = p.ManoObra, TotalRepuestos = detalles.Sum(d => d.Subtotal), Total = p.Total,
            Observaciones = p.Observaciones, Estado = p.Estado, FechaCreacion = p.FechaCreacion, CreadoPor = p.CreadoPor,
            ResueltoPor = p.ResueltoPor, FechaResolucion = p.FechaResolucion, ComentarioResolucion = p.ComentarioResolucion,
            InventarioDescontado = p.InventarioDescontado, Detalles = detalles
        };
    }

    public static RepuestoDto ToDto(this Repuesto r) => new()
    {
        Id = r.Id, Codigo = r.Codigo, Nombre = r.Nombre, Descripcion = r.Descripcion, Categoria = r.Categoria,
        Marca = r.Marca, Precio = r.Precio, Existencias = r.Existencias, StockMinimo = r.StockMinimo,
        FotoRuta = r.FotoRuta, Version = r.Version
    };

    public static MovimientoDto ToDto(this MovimientoInventario m) => new()
    {
        Id = m.Id, RepuestoId = m.RepuestoId, RepuestoCodigo = m.Repuesto?.Codigo ?? "", RepuestoNombre = m.Repuesto?.Nombre ?? "",
        Tipo = m.Tipo, Cantidad = m.Cantidad, ExistenciaAnterior = m.ExistenciaAnterior,
        ExistenciaResultante = m.ExistenciaResultante, Motivo = m.Motivo, OrdenId = m.OrdenId,
        NumeroOrden = m.Orden?.NumeroOrden, Usuario = m.Usuario, Fecha = m.Fecha
    };

    public static EvidenciaDto ToDto(this Evidencia e, string? numeroOrden = null) => new()
    {
        Id = e.Id, OrdenId = e.OrdenId, NumeroOrden = numeroOrden ?? e.Orden?.NumeroOrden ?? "", Tipo = e.Tipo,
        Ruta = e.Ruta, Descripcion = e.Descripcion, NombreOriginal = e.NombreOriginal, TamanoBytes = e.TamanoBytes,
        Fecha = e.Fecha, SubidoPor = e.SubidoPor
    };

    public static LogDto ToDto(this LogSolicitud l) => new()
    {
        Id = l.Id, Fecha = l.Fecha, Usuario = l.Usuario, Metodo = l.Metodo, Endpoint = l.Endpoint, Codigo = l.Codigo,
        DuracionMs = l.DuracionMs, CorrelationId = l.CorrelationId, Ip = l.Ip, Excepcion = l.Excepcion, Detalle = l.Detalle
    };
}
