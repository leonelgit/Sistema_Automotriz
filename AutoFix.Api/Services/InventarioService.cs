using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Services;

/// <summary>
/// Módulos 5 y 6 (escenario A). Toda salida/entrada de inventario pasa por aquí:
///  1. Se lee el repuesto (y su token Version).
///  2. Se valida en el backend que haya existencias suficientes.
///  3. Dentro de una TRANSACCIÓN se descuenta y se registra el movimiento.
///  4. Al guardar, EF Core ejecuta "UPDATE ... WHERE Id = @id AND Version = @versionLeida".
///     Si otro usuario descontó primero, el UPDATE no afecta filas -> DbUpdateConcurrencyException -> 409.
/// Además la tabla tiene un CHECK (Existencias >= 0): nunca puede quedar negativa.
/// </summary>
public class InventarioService
{
    private readonly AutoFixDbContext _db;
    private readonly ILogger<InventarioService> _logger;

    public InventarioService(AutoFixDbContext db, ILogger<InventarioService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MovimientoResultadoDto> RegistrarSalidaAsync(int repuestoId, MovimientoRequest r, string usuario)
    {
        if (r.Cantidad <= 0) throw new BusinessException("La cantidad debe ser mayor que cero.");

        var repuesto = await _db.Repuestos.FindAsync(repuestoId)
                       ?? throw new NotFoundException("El repuesto no existe.");

        AplicarVersionDelCliente(repuesto, r.Version);

        if (repuesto.Existencias < r.Cantidad)
            throw new BusinessException(
                $"Existencias insuficientes de '{repuesto.Nombre}'. Disponibles: {repuesto.Existencias}, solicitadas: {r.Cantidad}. No se realizó ningún cambio.");

        if (r.OrdenId.HasValue && !await _db.Ordenes.AnyAsync(o => o.Id == r.OrdenId))
            throw new NotFoundException("La orden indicada no existe.");

        // La transacción se abre DESPUÉS de leer: así dos peticiones simultáneas leen la misma
        // versión y la segunda en guardar recibe la excepción de concurrencia.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var mov = Descontar(repuesto, r.Cantidad, r.Motivo ?? "Salida manual", r.OrdenId, usuario);

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            _logger.LogWarning("Conflicto de concurrencia al descontar repuesto {RepuestoId} ({Cantidad} u.) por {Usuario}",
                repuestoId, r.Cantidad, usuario);
            throw new ConflictException("Otro usuario modificó el repuesto al mismo tiempo. La salida NO se aplicó; recargue e intente de nuevo.");
        }

        return Resultado(repuesto, mov, "Salida registrada");
    }

    public async Task<MovimientoResultadoDto> RegistrarEntradaAsync(int repuestoId, MovimientoRequest r, string usuario)
    {
        if (r.Cantidad <= 0) throw new BusinessException("La cantidad debe ser mayor que cero.");

        var repuesto = await _db.Repuestos.FindAsync(repuestoId)
                       ?? throw new NotFoundException("El repuesto no existe.");
        AplicarVersionDelCliente(repuesto, r.Version);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var mov = new MovimientoInventario
        {
            RepuestoId = repuesto.Id, Tipo = TipoMovimiento.Entrada, Cantidad = r.Cantidad,
            ExistenciaAnterior = repuesto.Existencias, ExistenciaResultante = repuesto.Existencias + r.Cantidad,
            Motivo = r.Motivo ?? "Entrada de mercadería", OrdenId = r.OrdenId, Usuario = usuario, Repuesto = repuesto
        };
        repuesto.Existencias += r.Cantidad;
        _db.Movimientos.Add(mov);

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            throw new ConflictException("Otro usuario modificó el repuesto al mismo tiempo. La entrada NO se aplicó; intente de nuevo.");
        }

        return Resultado(repuesto, mov, "Entrada registrada");
    }

    /// <summary>
    /// Descuenta todos los repuestos de un presupuesto aprobado (al iniciar la reparación).
    /// NO guarda: el llamador guarda dentro de su transacción junto con el cambio de estado,
    /// así todo se aplica o nada se aplica.
    /// </summary>
    public void DescontarPresupuesto(Presupuesto p, int ordenId, string numeroOrden, string usuario)
    {
        if (p.InventarioDescontado) return;

        // Validar TODO antes de modificar nada (agrupando por repuesto).
        var faltantes = p.Detalles
            .GroupBy(d => d.Repuesto)
            .Where(g => g.Key.Existencias < g.Sum(d => d.Cantidad))
            .Select(g => $"{g.Key.Nombre} (disponibles {g.Key.Existencias}, requeridas {g.Sum(d => d.Cantidad)})")
            .ToList();

        if (faltantes.Count > 0)
            throw new BusinessException("Existencias insuficientes para iniciar la reparación: " + string.Join("; ", faltantes) +
                                        ". No se descontó nada del inventario.");

        foreach (var d in p.Detalles)
            Descontar(d.Repuesto, d.Cantidad, $"Uso en orden {numeroOrden}", ordenId, usuario);

        p.InventarioDescontado = true;
    }

    /// <summary>Si una orden se cancela después de descontar, los repuestos regresan al inventario.</summary>
    public void DevolverPresupuesto(Presupuesto p, int ordenId, string numeroOrden, string usuario)
    {
        if (!p.InventarioDescontado) return;
        foreach (var d in p.Detalles)
        {
            _db.Movimientos.Add(new MovimientoInventario
            {
                RepuestoId = d.RepuestoId, Tipo = TipoMovimiento.Entrada, Cantidad = d.Cantidad,
                ExistenciaAnterior = d.Repuesto.Existencias, ExistenciaResultante = d.Repuesto.Existencias + d.Cantidad,
                Motivo = $"Devolución por cancelación de {numeroOrden}", OrdenId = ordenId, Usuario = usuario
            });
            d.Repuesto.Existencias += d.Cantidad;
        }
        p.InventarioDescontado = false;
    }

    private MovimientoInventario Descontar(Repuesto repuesto, int cantidad, string motivo, int? ordenId, string usuario)
    {
        if (repuesto.Existencias - cantidad < 0)
            throw new BusinessException($"Existencias insuficientes de '{repuesto.Nombre}'.");

        var mov = new MovimientoInventario
        {
            RepuestoId = repuesto.Id, Repuesto = repuesto, Tipo = TipoMovimiento.Salida, Cantidad = cantidad,
            ExistenciaAnterior = repuesto.Existencias, ExistenciaResultante = repuesto.Existencias - cantidad,
            Motivo = motivo, OrdenId = ordenId, Usuario = usuario
        };
        repuesto.Existencias -= cantidad;
        _db.Movimientos.Add(mov);

        if (repuesto.Existencias <= repuesto.StockMinimo)
            _logger.LogWarning("ALERTA DE STOCK: '{Repuesto}' quedó con {Existencias} unidades (mínimo {Minimo})",
                repuesto.Nombre, repuesto.Existencias, repuesto.StockMinimo);
        return mov;
    }

    /// <summary>
    /// Concurrencia optimista "desde el cliente": si el usuario envía la Version que vio en pantalla
    /// y ya no coincide con la actual, alguien modificó el repuesto mientras tanto => 409.
    /// </summary>
    private void AplicarVersionDelCliente(Repuesto repuesto, Guid? versionCliente)
    {
        if (!versionCliente.HasValue) return;
        if (versionCliente.Value != repuesto.Version)
            throw new ConflictException(
                $"El repuesto '{repuesto.Nombre}' fue modificado por otro usuario (existencias actuales: {repuesto.Existencias}). La operación NO se aplicó; recargue e intente de nuevo.");
        _db.Entry(repuesto).Property(x => x.Version).OriginalValue = versionCliente.Value;
    }

    private static MovimientoResultadoDto Resultado(Repuesto r, MovimientoInventario mov, string accion)
    {
        var alerta = r.Existencias <= r.StockMinimo;
        return new MovimientoResultadoDto
        {
            Movimiento = mov.ToDto(),
            ExistenciasActuales = r.Existencias,
            AlertaStockMinimo = alerta,
            Mensaje = alerta
                ? $"{accion}. ⚠ '{r.Nombre}' llegó al stock mínimo ({r.Existencias}/{r.StockMinimo})."
                : $"{accion}. Existencias actuales: {r.Existencias}."
        };
    }
}
