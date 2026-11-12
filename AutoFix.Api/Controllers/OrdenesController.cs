using AutoFix.Api.Data;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Módulo 3: órdenes de reparación, estados y asignación de mecánicos.</summary>
[ApiController]
[Route("api/ordenes")]
public class OrdenesController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly OrdenService _ordenes;
    private readonly PresupuestoService _presupuestos;

    public OrdenesController(AutoFixDbContext db, OrdenService ordenes, PresupuestoService presupuestos)
    {
        _db = db;
        _ordenes = ordenes;
        _presupuestos = presupuestos;
    }

    /// <summary>
    /// Lista órdenes. Cliente: solo las suyas. Mecánico: solo las asignadas a él.
    /// </summary>
    [HttpGet]
    public async Task<List<OrdenDto>> Listar([FromQuery] EstadoOrden? estado, [FromQuery] string? buscar,
        [FromQuery] int? mecanicoId, [FromQuery] int? clienteId, [FromQuery] int? vehiculoId, [FromQuery] bool soloActivas = false)
    {
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
        else
        {
            if (mecanicoId.HasValue) q = q.Where(o => o.MecanicoId == mecanicoId);
            if (clienteId.HasValue) q = q.Where(o => o.ClienteId == clienteId);
        }

        if (vehiculoId.HasValue) q = q.Where(o => o.VehiculoId == vehiculoId);
        if (estado.HasValue) q = q.Where(o => o.Estado == estado);
        if (soloActivas) q = q.Where(o => o.Estado != EstadoOrden.Entregado && o.Estado != EstadoOrden.Cancelado);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim().ToLower();
            q = q.Where(o => o.NumeroOrden.ToLower().Contains(b) || o.Vehiculo.Placa.ToLower().Contains(b)
                             || o.Cliente.NombreCompleto.ToLower().Contains(b) || o.TipoServicio.ToLower().Contains(b));
        }

        var lista = await q.OrderByDescending(o => o.Id).ToListAsync();
        return lista.Select(o => o.ToDto()).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrdenDetalleDto>> Obtener(int id)
    {
        var orden = await _ordenes.CargarDetalleAsync(id);
        OrdenService.VerificarAcceso(orden, User);
        return orden.ToDetalleDto();
    }

    /// <summary>Crea una orden en estado "Recibido". Rechaza duplicados activos (mismo vehículo + servicio).</summary>
    [HttpPost]
    [Authorize(Policy = Politicas.PuedeCrearOrdenes)]
    public async Task<ActionResult<OrdenDetalleDto>> Crear(CrearOrdenRequest r)
    {
        var orden = await _ordenes.CrearAsync(r, User.Email());
        var detalle = await _ordenes.CargarDetalleAsync(orden.Id);
        return CreatedAtAction(nameof(Obtener), new { id = orden.Id }, detalle.ToDetalleDto());
    }

    /// <summary>Modifica descripción, diagnóstico y fecha estimada. Requiere Ordenes.Editar (sin él => 403).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Politicas.PuedeEditarOrdenes)]
    public async Task<ActionResult<OrdenDetalleDto>> Editar(int id, EditarOrdenRequest r)
    {
        await _ordenes.EditarAsync(id, r, User);
        return (await _ordenes.CargarDetalleAsync(id)).ToDetalleDto();
    }

    /// <summary>Avanza el estado: EnDiagnostico, PendienteAprobacion, EnReparacion (descuenta inventario) o Entregado.</summary>
    [HttpPost("{id:int}/estado")]
    [Authorize(Policy = Politicas.PuedeEditarOrdenes)]
    public async Task<ActionResult<OrdenDetalleDto>> CambiarEstado(int id, CambiarEstadoRequest r)
    {
        await _ordenes.CambiarEstadoAsync(id, r, User);
        return (await _ordenes.CargarDetalleAsync(id)).ToDetalleDto();
    }

    /// <summary>Asigna un mecánico con horario (escenario B de concurrencia).</summary>
    [HttpPost("{id:int}/asignar")]
    [Authorize(Policy = Politicas.PuedeAsignar)]
    public async Task<ActionResult<AsignacionDto>> Asignar(int id, AsignarMecanicoRequest r)
    {
        var a = await _ordenes.AsignarMecanicoAsync(id, r, User.Email());
        return Ok(a.ToDto());
    }

    [HttpPost("{id:int}/finalizar")]
    [Authorize(Policy = Politicas.PuedeFinalizar)]
    public async Task<ActionResult<OrdenDetalleDto>> Finalizar(int id, ComentarioRequest? r = null)
    {
        await _ordenes.FinalizarAsync(id, r?.Comentario, User);
        return (await _ordenes.CargarDetalleAsync(id)).ToDetalleDto();
    }

    [HttpPost("{id:int}/cancelar")]
    [Authorize(Policy = Politicas.PuedeCancelar)]
    public async Task<ActionResult<OrdenDetalleDto>> Cancelar(int id, ComentarioRequest? r = null)
    {
        await _ordenes.CancelarAsync(id, r?.Comentario, User);
        return (await _ordenes.CargarDetalleAsync(id)).ToDetalleDto();
    }

    /// <summary>Solo el Administrador puede borrar, y solo órdenes recién recibidas o canceladas.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var orden = await _db.Ordenes.Include(o => o.Presupuesto).FirstOrDefaultAsync(o => o.Id == id)
                    ?? throw new NotFoundException("La orden no existe.");
        if (orden.Estado is not (EstadoOrden.Recibido or EstadoOrden.Cancelado))
            throw new BusinessException("Solo se pueden eliminar órdenes en estado Recibido o Cancelado.");
        if (orden.Presupuesto?.InventarioDescontado == true)
            throw new BusinessException("La orden tiene repuestos descontados; cancélela primero.");
        if (await _db.Movimientos.AnyAsync(m => m.OrdenId == id))
            throw new BusinessException("La orden tiene movimientos de inventario; no se puede eliminar.");
        _db.Ordenes.Remove(orden);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ───── Presupuesto de la orden ─────

    /// <summary>El cliente puede consultar su presupuesto y si fue aprobado o no.</summary>
    [HttpGet("{id:int}/presupuesto")]
    public async Task<ActionResult<PresupuestoDto>> ObtenerPresupuesto(int id)
    {
        var orden = await _ordenes.CargarDetalleAsync(id);
        OrdenService.VerificarAcceso(orden, User);
        if (orden.Presupuesto is null) throw new NotFoundException("La orden aún no tiene presupuesto.");
        return orden.Presupuesto.ToDto(orden);
    }

    /// <summary>Crea o reemplaza el presupuesto (mano de obra + repuestos). La orden pasa a "Presupuestado".</summary>
    [HttpPost("{id:int}/presupuesto")]
    [Authorize(Policy = Politicas.PuedeEditarOrdenes)]
    public async Task<ActionResult<PresupuestoDto>> CrearPresupuesto(int id, CrearPresupuestoRequest r)
    {
        var p = await _presupuestos.CrearOReemplazarAsync(id, r, User);
        return Ok((await _presupuestos.CargarAsync(p.Id)).ToDto());
    }
}
