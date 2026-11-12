using AutoFix.Api.Data;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Consultar movimientos (entradas y salidas) de inventario.</summary>
[ApiController]
[Route("api/movimientos")]
[Authorize(Policy = Politicas.Empleados)]
public class MovimientosController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    public MovimientosController(AutoFixDbContext db) => _db = db;

    [HttpGet]
    public async Task<List<MovimientoDto>> Listar([FromQuery] int? repuestoId, [FromQuery] TipoMovimiento? tipo,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] int? ordenId, [FromQuery] int take = 200)
    {
        var q = _db.Movimientos.Include(m => m.Repuesto).Include(m => m.Orden).AsQueryable();
        if (repuestoId.HasValue) q = q.Where(m => m.RepuestoId == repuestoId);
        if (ordenId.HasValue) q = q.Where(m => m.OrdenId == ordenId);
        if (tipo.HasValue) q = q.Where(m => m.Tipo == tipo);
        if (desde.HasValue) q = q.Where(m => m.Fecha >= desde.Value);
        if (hasta.HasValue) { var h = hasta.Value.Date.AddDays(1); q = q.Where(m => m.Fecha < h); }
        take = Math.Clamp(take, 1, 1000);
        return (await q.OrderByDescending(m => m.Id).Take(take).ToListAsync()).Select(m => m.ToDto()).ToList();
    }
}
