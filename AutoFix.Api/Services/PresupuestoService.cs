using System.Security.Claims;
using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Security;
using AutoFix.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Services;

/// <summary>Módulo 4. Presupuestos: mano de obra + repuestos (cantidad, precio unitario, subtotal) y total.</summary>
public class PresupuestoService
{
    private readonly AutoFixDbContext _db;
    private readonly OrdenService _ordenes;

    public PresupuestoService(AutoFixDbContext db, OrdenService ordenes)
    {
        _db = db;
        _ordenes = ordenes;
    }

    public async Task<Presupuesto> CargarAsync(int id) =>
        await _db.Presupuestos
            .Include(p => p.Detalles).ThenInclude(d => d.Repuesto)
            .Include(p => p.Orden).ThenInclude(o => o.Cliente)
            .Include(p => p.Orden).ThenInclude(o => o.Vehiculo)
            .FirstOrDefaultAsync(p => p.Id == id)
        ?? throw new NotFoundException("El presupuesto no existe.");

    /// <summary>Crea (o reemplaza, si aún no está aprobado) el presupuesto de una orden.</summary>
    public async Task<Presupuesto> CrearOReemplazarAsync(int ordenId, CrearPresupuestoRequest r, ClaimsPrincipal user)
    {
        var orden = await _ordenes.CargarDetalleAsync(ordenId);
        OrdenService.VerificarAcceso(orden, user, paraModificar: true);

        if (orden.Estado is not (EstadoOrden.EnDiagnostico or EstadoOrden.Presupuestado))
            throw new BusinessException(
                $"Solo se puede presupuestar una orden 'En diagnóstico' o 'Presupuestado' (estado actual: '{orden.Estado.Texto()}').");

        if (orden.Presupuesto is { Estado: EstadoPresupuesto.Aprobado })
            throw new BusinessException("El presupuesto ya fue aprobado y no puede modificarse.");

        if (r.ManoObra < 0) throw new BusinessException("La mano de obra no puede ser negativa.");

        // Unificar líneas repetidas del mismo repuesto.
        var lineas = r.Repuestos
            .Where(l => l.RepuestoId > 0 && l.Cantidad > 0)
            .GroupBy(l => l.RepuestoId)
            .Select(g => new { RepuestoId = g.Key, Cantidad = g.Sum(x => x.Cantidad), Precio = g.First().PrecioUnitario })
            .ToList();

        var ids = lineas.Select(l => l.RepuestoId).ToList();
        var repuestos = await _db.Repuestos.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var noExisten = ids.Where(id => !repuestos.ContainsKey(id)).ToList();
        if (noExisten.Count > 0)
            throw new NotFoundException($"Repuestos inexistentes: {string.Join(", ", noExisten)}");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var p = orden.Presupuesto;
        if (p is null)
        {
            p = new Presupuesto { OrdenId = orden.Id };
            _db.Presupuestos.Add(p);
            orden.Presupuesto = p;
        }
        else
        {
            _db.DetallesPresupuesto.RemoveRange(p.Detalles);
            p.Detalles.Clear();
        }

        p.ManoObra = Math.Round(r.ManoObra, 2);
        p.Observaciones = r.Observaciones?.Trim();
        p.Estado = EstadoPresupuesto.Pendiente;
        p.FechaCreacion = DateTime.Now;
        p.CreadoPor = user.Email();
        p.ResueltoPor = null;
        p.FechaResolucion = null;
        p.ComentarioResolucion = null;

        foreach (var l in lineas)
        {
            var rep = repuestos[l.RepuestoId];
            var precio = Math.Round(l.Precio ?? rep.Precio, 2);
            p.Detalles.Add(new DetallePresupuesto
            {
                RepuestoId = rep.Id, Repuesto = rep, Cantidad = l.Cantidad,
                PrecioUnitario = precio, Subtotal = Math.Round(precio * l.Cantidad, 2)
            });
        }
        p.Total = p.ManoObra + p.Detalles.Sum(d => d.Subtotal);

        if (orden.Estado == EstadoOrden.EnDiagnostico)
            _ordenes.RegistrarCambio(orden, EstadoOrden.Presupuestado, user.Email(), $"Presupuesto registrado por ${p.Total:N2}");

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return p;
    }

    public async Task<Presupuesto> AprobarAsync(int id, string? comentario, string usuario)
    {
        var p = await CargarAsync(id);
        if (p.Orden.Estado != EstadoOrden.PendienteAprobacion || p.Estado != EstadoPresupuesto.Pendiente)
            throw new BusinessException(
                $"Solo se aprueba un presupuesto pendiente con la orden 'Pendiente de aprobación' (orden: '{p.Orden.Estado.Texto()}').");

        await using var tx = await _db.Database.BeginTransactionAsync();
        p.Estado = EstadoPresupuesto.Aprobado;
        p.ResueltoPor = usuario;
        p.FechaResolucion = DateTime.Now;
        p.ComentarioResolucion = comentario;
        _ordenes.RegistrarCambio(p.Orden, EstadoOrden.Aprobado, usuario, comentario ?? "Presupuesto aprobado");

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            throw new ConflictException("La orden fue modificada por otro usuario mientras se aprobaba. Recargue e intente de nuevo.");
        }
        return p;
    }

    public async Task<Presupuesto> RechazarAsync(int id, string? comentario, string usuario)
    {
        if (string.IsNullOrWhiteSpace(comentario))
            throw new BusinessException("Indique el motivo del rechazo.");

        var p = await CargarAsync(id);
        if (p.Orden.Estado != EstadoOrden.PendienteAprobacion || p.Estado != EstadoPresupuesto.Pendiente)
            throw new BusinessException("Solo se puede rechazar un presupuesto pendiente de aprobación.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        p.Estado = EstadoPresupuesto.Rechazado;
        p.ResueltoPor = usuario;
        p.FechaResolucion = DateTime.Now;
        p.ComentarioResolucion = comentario;
        // La orden regresa a "Presupuestado" para que se corrija el presupuesto.
        _ordenes.RegistrarCambio(p.Orden, EstadoOrden.Presupuestado, usuario, "Presupuesto rechazado: " + comentario);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return p;
    }
}
