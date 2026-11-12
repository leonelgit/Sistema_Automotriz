using AutoFix.Api.Data;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Módulo 4: presupuestos. Aprobar exige rol Supervisor/Administrador + claim Ordenes.Aprobar.</summary>
[ApiController]
[Route("api/presupuestos")]
public class PresupuestosController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly PresupuestoService _presupuestos;
    private readonly OrdenService _ordenes;

    public PresupuestosController(AutoFixDbContext db, PresupuestoService presupuestos, OrdenService ordenes)
    {
        _db = db;
        _presupuestos = presupuestos;
        _ordenes = ordenes;
    }

    [HttpGet]
    public async Task<List<PresupuestoDto>> Listar([FromQuery] EstadoPresupuesto? estado)
    {
        var q = _db.Presupuestos
            .Include(p => p.Detalles).ThenInclude(d => d.Repuesto)
            .Include(p => p.Orden).ThenInclude(o => o.Cliente)
            .Include(p => p.Orden).ThenInclude(o => o.Vehiculo)
            .AsSplitQuery()
            .AsQueryable();

        if (User.EsCliente())
        {
            var miCliente = User.ClienteId();
            q = q.Where(p => p.Orden.ClienteId == miCliente);
        }
        else if (User.EsMecanico())
        {
            var miMecanico = User.MecanicoId();
            q = q.Where(p => p.Orden.MecanicoId == miMecanico);
        }
        if (estado.HasValue) q = q.Where(p => p.Estado == estado);

        return (await q.OrderByDescending(p => p.Id).ToListAsync()).Select(p => p.ToDto()).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PresupuestoDto>> Obtener(int id)
    {
        var p = await _presupuestos.CargarAsync(id);
        OrdenService.VerificarAcceso(p.Orden, User);
        return p.ToDto();
    }

    /// <summary>Envía el presupuesto a aprobación (orden: Presupuestado -> Pendiente de aprobación).</summary>
    [HttpPost("{id:int}/enviar")]
    [Authorize(Policy = Politicas.PuedeEditarOrdenes)]
    public async Task<ActionResult<PresupuestoDto>> Enviar(int id, ComentarioRequest? r = null)
    {
        var p = await _presupuestos.CargarAsync(id);
        await _ordenes.CambiarEstadoAsync(p.OrdenId,
            new CambiarEstadoRequest { NuevoEstado = EstadoOrden.PendienteAprobacion, Comentario = r?.Comentario ?? "Enviado a aprobación" }, User);
        return (await _presupuestos.CargarAsync(id)).ToDto();
    }

    [Authorize(Policy = Politicas.PuedeAprobar)]
    [HttpPost("{id:int}/aprobar")]
    public async Task<ActionResult<PresupuestoDto>> Aprobar(int id, ComentarioRequest? r = null)
    {
        await _presupuestos.AprobarAsync(id, r?.Comentario, User.Email());
        return (await _presupuestos.CargarAsync(id)).ToDto();
    }

    [Authorize(Policy = Politicas.PuedeAprobar)]
    [HttpPost("{id:int}/rechazar")]
    public async Task<ActionResult<PresupuestoDto>> Rechazar(int id, ComentarioRequest? r = null)
    {
        await _presupuestos.RechazarAsync(id, r?.Comentario, User.Email());
        return (await _presupuestos.CargarAsync(id)).ToDto();
    }
}
