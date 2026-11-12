using System.Security.Claims;
using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Security;
using AutoFix.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Services;

/// <summary>
/// Reglas de negocio de las órdenes de reparación:
///  - Problema 1: evita órdenes duplicadas (mismo vehículo + mismo servicio con orden activa).
///  - Problema 2 / Escenario B: asignación de mecánicos sin choques de horario y con concurrencia optimista.
///  - Problema 3: flujo de estados validado en el backend (nadie se salta pasos).
/// </summary>
public class OrdenService
{
    private readonly AutoFixDbContext _db;
    private readonly InventarioService _inventario;
    private readonly ILogger<OrdenService> _logger;

    public OrdenService(AutoFixDbContext db, InventarioService inventario, ILogger<OrdenService> logger)
    {
        _db = db;
        _inventario = inventario;
        _logger = logger;
    }

    // ───────────────────────── Consultas ─────────────────────────

    public IQueryable<OrdenReparacion> Consulta() => _db.Ordenes
        .Include(o => o.Cliente)
        .Include(o => o.Vehiculo)
        .Include(o => o.Mecanico)
        .Include(o => o.Presupuesto);

    public async Task<OrdenReparacion> CargarDetalleAsync(int id)
    {
        return await _db.Ordenes
                   .Include(o => o.Cliente)
                   .Include(o => o.Vehiculo)
                   .Include(o => o.Mecanico)
                   .Include(o => o.Historial)
                   .Include(o => o.Evidencias)
                   .Include(o => o.Asignaciones).ThenInclude(a => a.Mecanico)
                   .Include(o => o.Presupuesto!).ThenInclude(p => p.Detalles).ThenInclude(d => d.Repuesto)
                   .AsSplitQuery()
                   .FirstOrDefaultAsync(o => o.Id == id)
               ?? throw new NotFoundException("La orden no existe.");
    }

    /// <summary>
    /// Cliente: solo sus órdenes. Mecánico: solo las que tiene asignadas.
    /// </summary>
    public static void VerificarAcceso(OrdenReparacion o, ClaimsPrincipal user, bool paraModificar = false)
    {
        if (user.EsCliente())
        {
            if (paraModificar || user.ClienteId() != o.ClienteId)
                throw new ForbiddenException("No tiene acceso a esta orden.");
        }
        else if (user.EsMecanico() && o.MecanicoId != user.MecanicoId())
        {
            throw new ForbiddenException("Solo puede consultar o actualizar las reparaciones que tiene asignadas.");
        }
    }

    // ───────────────────────── Crear (problema 1) ─────────────────────────

    public async Task<OrdenReparacion> CrearAsync(CrearOrdenRequest r, string usuario)
    {
        var servicio = r.TipoServicio.Trim();
        if (r.FechaEstimadaFin <= DateTime.Now)
            throw new BusinessException("La fecha estimada de finalización debe ser futura.");

        // En SQLite BeginTransaction = "BEGIN IMMEDIATE": se toma el bloqueo de escritura desde el inicio,
        // así dos recepcionistas que crean la misma orden a la vez quedan en fila y el segundo ve el duplicado.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var vehiculo = await _db.Vehiculos.FirstOrDefaultAsync(v => v.Id == r.VehiculoId)
                       ?? throw new NotFoundException("El vehículo no existe.");

        var servicioLower = servicio.ToLower();
        var duplicada = await _db.Ordenes
            .Where(o => o.VehiculoId == r.VehiculoId
                        && o.TipoServicio.ToLower() == servicioLower
                        && o.Estado != EstadoOrden.Entregado
                        && o.Estado != EstadoOrden.Cancelado)
            .Select(o => o.NumeroOrden)
            .FirstOrDefaultAsync();

        if (duplicada != null)
            throw new ConflictException(
                $"Ya existe la orden activa {duplicada} para el vehículo {vehiculo.Placa} con el servicio '{servicio}'. No se creó una orden duplicada.");

        var orden = new OrdenReparacion
        {
            NumeroOrden = "TMP-" + Guid.NewGuid().ToString("N"),
            ClienteId = vehiculo.ClienteId,
            VehiculoId = vehiculo.Id,
            TipoServicio = servicio,
            FechaIngreso = DateTime.Now,
            FechaEstimadaFin = r.FechaEstimadaFin,
            DescripcionProblema = r.DescripcionProblema.Trim(),
            Estado = EstadoOrden.Recibido,
            CreadoPor = usuario
        };
        orden.Historial.Add(new HistorialEstado
        {
            EstadoAnterior = null, EstadoNuevo = EstadoOrden.Recibido, Usuario = usuario, Comentario = "Orden registrada"
        });

        _db.Ordenes.Add(orden);
        await _db.SaveChangesAsync();

        orden.NumeroOrden = $"OR-{DateTime.Now:yyyy}-{orden.Id:D5}";
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation("Orden {Numero} creada por {Usuario}", orden.NumeroOrden, usuario);
        return orden;
    }

    // ───────────────────────── Editar ─────────────────────────

    public async Task<OrdenReparacion> EditarAsync(int id, EditarOrdenRequest r, ClaimsPrincipal user)
    {
        var orden = await _db.Ordenes.FindAsync(id) ?? throw new NotFoundException("La orden no existe.");
        VerificarAcceso(orden, user, paraModificar: true);

        if (!orden.Estado.EsActiva() || orden.Estado == EstadoOrden.Finalizado)
            throw new BusinessException($"No se puede modificar una orden en estado '{orden.Estado.Texto()}'.");

        // Concurrencia optimista: comparamos con la versión que el usuario tenía en pantalla.
        if (r.Version != Guid.Empty)
            _db.Entry(orden).Property(o => o.Version).OriginalValue = r.Version;

        orden.DescripcionProblema = r.DescripcionProblema.Trim();
        orden.Diagnostico = r.Diagnostico?.Trim();
        orden.FechaEstimadaFin = r.FechaEstimadaFin;

        await _db.SaveChangesAsync(); // DbUpdateConcurrencyException => 409 en el middleware
        return orden;
    }

    // ───────────────────────── Estados (problema 3) ─────────────────────────

    /// <summary>
    /// Transiciones "normales" permitidas por este endpoint. Las demás tienen su propio endpoint
    /// con su propia política: Presupuestado (crear presupuesto), Aprobado (aprobar),
    /// Finalizado (finalizar) y Cancelado (cancelar).
    /// </summary>
    private static readonly Dictionary<EstadoOrden, EstadoOrden> TransicionesGenericas = new()
    {
        [EstadoOrden.EnDiagnostico] = EstadoOrden.Recibido,
        [EstadoOrden.PendienteAprobacion] = EstadoOrden.Presupuestado,
        [EstadoOrden.EnReparacion] = EstadoOrden.Aprobado,
        [EstadoOrden.Entregado] = EstadoOrden.Finalizado
    };

    public async Task<OrdenReparacion> CambiarEstadoAsync(int id, CambiarEstadoRequest r, ClaimsPrincipal user)
    {
        var orden = await CargarDetalleAsync(id);
        VerificarAcceso(orden, user, paraModificar: true);

        if (!TransicionesGenericas.TryGetValue(r.NuevoEstado, out var requerido))
            throw new BusinessException(r.NuevoEstado switch
            {
                EstadoOrden.Presupuestado => "Para pasar a 'Presupuestado' registre el presupuesto (POST /api/ordenes/{id}/presupuesto).",
                EstadoOrden.Aprobado => "La aprobación se hace con POST /api/presupuestos/{id}/aprobar (requiere permiso Ordenes.Aprobar).",
                EstadoOrden.Finalizado => "Para finalizar use POST /api/ordenes/{id}/finalizar (requiere permiso Ordenes.Finalizar).",
                EstadoOrden.Cancelado => "Para cancelar use POST /api/ordenes/{id}/cancelar.",
                _ => "Estado no válido."
            });

        if (orden.Estado != requerido)
            throw new BusinessException(
                $"No se puede pasar de '{orden.Estado.Texto()}' a '{r.NuevoEstado.Texto()}'. La orden debe estar en '{requerido.Texto()}'. No se permiten saltos de pasos.");

        if (r.NuevoEstado == EstadoOrden.Entregado && user.EsMecanico())
            throw new ForbiddenException("La entrega del vehículo al cliente la registra recepción o supervisión.");

        if (r.Version.HasValue && r.Version != Guid.Empty)
            _db.Entry(orden).Property(o => o.Version).OriginalValue = r.Version.Value;

        await using var tx = await _db.Database.BeginTransactionAsync();

        switch (r.NuevoEstado)
        {
            case EstadoOrden.PendienteAprobacion:
                if (orden.Presupuesto is null)
                    throw new BusinessException("La orden no tiene presupuesto.");
                orden.Presupuesto.Estado = EstadoPresupuesto.Pendiente;
                break;

            case EstadoOrden.EnReparacion:
                if (orden.MecanicoId is null)
                    throw new BusinessException("Asigne un mecánico antes de iniciar la reparación.");
                if (orden.Presupuesto is not { Estado: EstadoPresupuesto.Aprobado })
                    throw new BusinessException("El presupuesto debe estar aprobado.");
                // Descuento automático de inventario (módulo 4) dentro de la misma transacción.
                _inventario.DescontarPresupuesto(orden.Presupuesto, orden.Id, orden.NumeroOrden, user.Email());
                break;
        }

        RegistrarCambio(orden, r.NuevoEstado, user.Email(), r.Comentario);

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            throw new ConflictException("La orden o alguno de sus repuestos fue modificado por otro usuario al mismo tiempo. No se aplicó el cambio; recargue e intente de nuevo.");
        }
        return orden;
    }

    public async Task<OrdenReparacion> FinalizarAsync(int id, string? comentario, ClaimsPrincipal user)
    {
        var orden = await CargarDetalleAsync(id);
        if (orden.Estado != EstadoOrden.EnReparacion)
            throw new BusinessException($"Solo se puede finalizar una orden 'En reparación' (estado actual: '{orden.Estado.Texto()}').");

        await using var tx = await _db.Database.BeginTransactionAsync();
        orden.FechaFinalizacion = DateTime.Now;
        foreach (var a in orden.Asignaciones.Where(a => a.Activa))
        {
            a.Activa = false;
            if (a.Fin > DateTime.Now) a.Fin = DateTime.Now; // libera la agenda del mecánico
        }
        RegistrarCambio(orden, EstadoOrden.Finalizado, user.Email(), comentario ?? "Reparación finalizada");

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return orden;
    }

    public async Task<OrdenReparacion> CancelarAsync(int id, string? motivo, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessException("Indique el motivo de la cancelación.");

        var orden = await CargarDetalleAsync(id);
        if (orden.Estado is EstadoOrden.Entregado or EstadoOrden.Cancelado or EstadoOrden.Finalizado)
            throw new BusinessException($"No se puede cancelar una orden en estado '{orden.Estado.Texto()}'.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        if (orden.Presupuesto != null)
            _inventario.DevolverPresupuesto(orden.Presupuesto, orden.Id, orden.NumeroOrden, user.Email());
        foreach (var a in orden.Asignaciones.Where(a => a.Activa)) a.Activa = false;
        RegistrarCambio(orden, EstadoOrden.Cancelado, user.Email(), "Cancelada: " + motivo);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return orden;
    }

    public void RegistrarCambio(OrdenReparacion orden, EstadoOrden nuevo, string usuario, string? comentario)
    {
        _db.HistorialEstados.Add(new HistorialEstado
        {
            OrdenId = orden.Id, EstadoAnterior = orden.Estado, EstadoNuevo = nuevo,
            Usuario = usuario, Comentario = comentario, Fecha = DateTime.Now
        });
        _logger.LogInformation("Orden {Numero}: {Anterior} -> {Nuevo} por {Usuario}",
            orden.NumeroOrden, orden.Estado, nuevo, usuario);
        orden.Estado = nuevo;
    }

    // ───────────────────────── Asignación (problema 2 / escenario B) ─────────────────────────

    public async Task<AsignacionMecanico> AsignarMecanicoAsync(int ordenId, AsignarMecanicoRequest r, string usuario)
    {
        if (r.Fin <= r.Inicio)
            throw new BusinessException("La hora de fin debe ser posterior a la de inicio.");

        var orden = await _db.Ordenes.FindAsync(ordenId) ?? throw new NotFoundException("La orden no existe.");
        if (orden.Estado is EstadoOrden.Finalizado or EstadoOrden.Entregado or EstadoOrden.Cancelado)
            throw new BusinessException($"No se puede asignar mecánico a una orden '{orden.Estado.Texto()}'.");

        // 1) Leer el mecánico (con su token Version).
        var mecanico = await _db.Mecanicos.FindAsync(r.MecanicoId) ?? throw new NotFoundException("El mecánico no existe.");
        if (!mecanico.Activo) throw new BusinessException("El mecánico no está activo.");

        if (r.MecanicoVersion.HasValue && r.MecanicoVersion.Value != mecanico.Version)
            throw new ConflictException(
                $"La agenda de {mecanico.Nombre} cambió mientras usted la consultaba (otro supervisor lo asignó). Recargue e intente de nuevo.");

        // 2) Validar disponibilidad: hay choque si nuevoInicio < finExistente Y inicioExistente < nuevoFin.
        var choque = await _db.Asignaciones
            .Include(a => a.Orden)
            .Where(a => a.MecanicoId == mecanico.Id && a.Activa && a.OrdenId != ordenId
                        && r.Inicio < a.Fin && a.Inicio < r.Fin)
            .FirstOrDefaultAsync();

        if (choque != null)
            throw new ConflictException(
                $"{mecanico.Nombre} ya está asignado a la orden {choque.Orden.NumeroOrden} de {choque.Inicio:dd/MM HH:mm} a {choque.Fin:dd/MM HH:mm}. El horario se cruza.");

        // 3) Transacción: guardar asignación + "tocar" la fila del mecánico para cambiar su Version.
        //    Si dos supervisores asignan a la vez, ambos leyeron la misma Version y el segundo
        //    UPDATE Mecanicos ... WHERE Version = @leida no afecta filas => DbUpdateConcurrencyException.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var previas = await _db.Asignaciones.Where(a => a.OrdenId == ordenId && a.Activa).ToListAsync();
        foreach (var p in previas) p.Activa = false;

        var asignacion = new AsignacionMecanico
        {
            MecanicoId = mecanico.Id, OrdenId = ordenId, Inicio = r.Inicio, Fin = r.Fin,
            Activa = true, AsignadoPor = usuario, FechaAsignacion = DateTime.Now
        };
        _db.Asignaciones.Add(asignacion);

        orden.MecanicoId = mecanico.Id;
        mecanico.UltimaAsignacion = DateTime.Now; // modifica la fila => nuevo token Version

        _db.HistorialEstados.Add(new HistorialEstado
        {
            OrdenId = orden.Id, EstadoAnterior = orden.Estado, EstadoNuevo = orden.Estado, Usuario = usuario,
            Comentario = $"Mecánico asignado: {mecanico.Nombre} ({r.Inicio:dd/MM/yyyy HH:mm} - {r.Fin:HH:mm})"
        });

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            _logger.LogWarning("Conflicto de concurrencia asignando al mecánico {MecanicoId} a la orden {OrdenId} ({Usuario})",
                mecanico.Id, ordenId, usuario);
            throw new ConflictException(
                $"Otro supervisor asignó a {mecanico.Nombre} al mismo tiempo. Esta asignación NO se aplicó; revise la agenda e intente de nuevo.");
        }

        asignacion.Mecanico = mecanico;
        asignacion.Orden = orden;
        return asignacion;
    }
}
