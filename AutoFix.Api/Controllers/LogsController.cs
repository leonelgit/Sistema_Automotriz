using AutoFix.Api.Data;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Módulo 8: consulta de logs (auditoría). Filtra por Correlation ID, usuario, código o solo errores.</summary>
[ApiController]
[Route("api/logs")]
[Authorize(Policy = Politicas.PuedeConsultarReportes)]
public class LogsController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    public LogsController(AutoFixDbContext db) => _db = db;

    [HttpGet]
    public async Task<List<LogDto>> Listar([FromQuery] string? correlationId, [FromQuery] string? usuario,
        [FromQuery] int? codigo, [FromQuery] bool soloErrores = false, [FromQuery] int take = 100)
    {
        var q = _db.LogsSolicitudes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(correlationId)) q = q.Where(l => l.CorrelationId == correlationId.Trim());
        if (!string.IsNullOrWhiteSpace(usuario)) q = q.Where(l => l.Usuario.Contains(usuario.Trim()));
        if (codigo.HasValue) q = q.Where(l => l.Codigo == codigo);
        if (soloErrores) q = q.Where(l => l.Codigo >= 400);
        take = Math.Clamp(take, 1, 1000);
        return (await q.OrderByDescending(l => l.Id).Take(take).ToListAsync()).Select(l => l.ToDto()).ToList();
    }
}
