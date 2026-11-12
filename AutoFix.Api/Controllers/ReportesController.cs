using AutoFix.Api.Data;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>
/// Reportes. Nota SQLite: los decimal se guardan como TEXT, por eso las sumas de dinero
/// se hacen en memoria (después de ToListAsync) y no en SQL.
/// </summary>
[ApiController]
[Route("api/reportes")]
public class ReportesController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly OrdenService _ordenes;

    public ReportesController(AutoFixDbContext db, OrdenService ordenes)
    {
        _db = db;
        _ordenes = ordenes;
    }

    /// <summary>Resumen del taller para el dashboard (cualquier usuario autenticado; el cliente ve solo lo suyo).</summary>
    [HttpGet("dashboard")]
    public async Task<DashboardDto> Dashboard()
    {
        var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var q = _ordenes.Consulta();
        if (User.EsCliente())
        {
            var miCliente = User.ClienteId();
            q = q.Where(o => o.ClienteId == miCliente);
        }
        else if (User.EsMecanico())
        {
            var miMecanico = User.MecanicoId();
            q = q.Where(o => o.MecanicoId == miMecanico);
        }

        var ordenes = await q.ToListAsync();
        var dto = new DashboardDto
        {
            OrdenesActivas = ordenes.Count(o => o.Estado.EsActiva() && o.Estado != EstadoOrden.Finalizado),
            OrdenesFinalizadasMes = ordenes.Count(o => o.FechaFinalizacion >= inicioMes),
            PresupuestosPendientes = ordenes.Count(o => o.Estado == EstadoOrden.PendienteAprobacion),
            IngresosAprobadosMes = ordenes.Where(o => o.Presupuesto is { Estado: EstadoPresupuesto.Aprobado } p && p.FechaResolucion >= inicioMes)
                                          .Sum(o => o.Presupuesto!.Total),
            OrdenesPorEstado = Enum.GetValues<EstadoOrden>().ToDictionary(e => e.Texto(), e => ordenes.Count(o => o.Estado == e)),
            OrdenesRecientes = ordenes.OrderByDescending(o => o.Id).Take(8).Select(o => o.ToDto()).ToList()
        };

        if (User.EsCliente())
        {
            var miCliente = User.ClienteId();
            dto.TotalClientes = 1;
            dto.TotalVehiculos = await _db.Vehiculos.CountAsync(v => v.ClienteId == miCliente);
        }
        else
        {
            dto.TotalClientes = await _db.Clientes.CountAsync();
            dto.TotalVehiculos = await _db.Vehiculos.CountAsync();
            dto.MecanicosActivos = await _db.Mecanicos.CountAsync(m => m.Activo);
            dto.RepuestosBajoStock = (await _db.Repuestos.Where(r => r.Activo && r.Existencias <= r.StockMinimo).ToListAsync())
                .Select(r => r.ToDto()).ToList();
        }
        return dto;
    }

    [HttpGet("ordenes")]
    [Authorize(Policy = Politicas.PuedeConsultarReportes)]
    public async Task<ReporteOrdenesDto> Ordenes([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] EstadoOrden? estado)
    {
        var d = (desde ?? DateTime.Today.AddDays(-30)).Date;
        var h = (hasta ?? DateTime.Today).Date;
        var hExclusivo = h.AddDays(1);

        var q = _ordenes.Consulta().Where(o => o.FechaIngreso >= d && o.FechaIngreso < hExclusivo);
        if (estado.HasValue) q = q.Where(o => o.Estado == estado);
        var lista = await q.OrderByDescending(o => o.FechaIngreso).ToListAsync();

        return new ReporteOrdenesDto
        {
            Desde = d, Hasta = h, Total = lista.Count,
            MontoPresupuestosAprobados = lista.Where(o => o.Presupuesto?.Estado == EstadoPresupuesto.Aprobado).Sum(o => o.Presupuesto!.Total),
            PorEstado = lista.GroupBy(o => o.Estado.Texto()).ToDictionary(g => g.Key, g => g.Count()),
            Ordenes = lista.Select(o => o.ToDto()).ToList()
        };
    }

    [HttpGet("mecanicos")]
    [Authorize(Policy = Politicas.PuedeConsultarReportes)]
    public async Task<List<ReporteMecanicoDto>> Mecanicos()
    {
        var ahora = DateTime.Now;
        var mecanicos = await _db.Mecanicos.OrderBy(m => m.Nombre).ToListAsync();
        var ordenes = await _db.Ordenes.Where(o => o.MecanicoId != null).Select(o => new { o.MecanicoId, o.Estado }).ToListAsync();
        var asignaciones = await _db.Asignaciones.Where(a => a.Activa && a.Fin > ahora).Select(a => a.MecanicoId).ToListAsync();

        return mecanicos.Select(m => new ReporteMecanicoDto
        {
            MecanicoId = m.Id, Nombre = m.Nombre, Activo = m.Activo,
            OrdenesActivas = ordenes.Count(o => o.MecanicoId == m.Id && o.Estado.EsActiva() && o.Estado != EstadoOrden.Finalizado),
            OrdenesFinalizadas = ordenes.Count(o => o.MecanicoId == m.Id && o.Estado is EstadoOrden.Finalizado or EstadoOrden.Entregado),
            AsignacionesFuturas = asignaciones.Count(id => id == m.Id)
        }).ToList();
    }

    [HttpGet("inventario")]
    [Authorize(Policy = Politicas.PuedeConsultarReportes)]
    public async Task<ReporteInventarioDto> Inventario()
    {
        var repuestos = await _db.Repuestos.Where(r => r.Activo).ToListAsync();
        var movs = await _db.Movimientos.Include(m => m.Repuesto).Include(m => m.Orden)
            .OrderByDescending(m => m.Id).Take(15).ToListAsync();
        return new ReporteInventarioDto
        {
            TotalRepuestos = repuestos.Count,
            UnidadesTotales = repuestos.Sum(r => r.Existencias),
            ValorInventario = repuestos.Sum(r => r.Precio * r.Existencias),
            BajoStock = repuestos.Where(r => r.Existencias <= r.StockMinimo).Select(r => r.ToDto()).ToList(),
            UltimosMovimientos = movs.Select(m => m.ToDto()).ToList()
        };
    }
}
