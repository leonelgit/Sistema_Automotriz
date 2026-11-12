using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Administración de mecánicos y consulta de su agenda (asignaciones).</summary>
[ApiController]
[Route("api/mecanicos")]
[Authorize(Policy = Politicas.Empleados)]
public class MecanicosController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    public MecanicosController(AutoFixDbContext db) => _db = db;

    [HttpGet]
    public async Task<List<MecanicoDto>> Listar([FromQuery] bool soloActivos = false)
    {
        var q = _db.Mecanicos.AsQueryable();
        if (soloActivos) q = q.Where(m => m.Activo);
        var mecanicos = await q.OrderBy(m => m.Nombre).ToListAsync();

        var activas = await _db.Ordenes
            .Where(o => o.MecanicoId != null && o.Estado != EstadoOrden.Entregado
                        && o.Estado != EstadoOrden.Cancelado && o.Estado != EstadoOrden.Finalizado)
            .GroupBy(o => o.MecanicoId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Total);

        var correos = await _db.Usuarios.Where(u => u.MecanicoId != null)
            .ToDictionaryAsync(u => u.MecanicoId!.Value, u => u.Email);

        return mecanicos.Select(m => m.ToDto(activas.GetValueOrDefault(m.Id), correos.GetValueOrDefault(m.Id))).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MecanicoDto>> Obtener(int id)
    {
        var m = await _db.Mecanicos.FindAsync(id) ?? throw new NotFoundException("El mecánico no existe.");
        var activas = await _db.Ordenes.CountAsync(o => o.MecanicoId == id && o.Estado != EstadoOrden.Entregado
                                                        && o.Estado != EstadoOrden.Cancelado && o.Estado != EstadoOrden.Finalizado);
        var email = await _db.Usuarios.Where(u => u.MecanicoId == id).Select(u => u.Email).FirstOrDefaultAsync();
        return m.ToDto(activas, email);
    }

    /// <summary>Agenda del mecánico (asignaciones con inicio y fin).</summary>
    [HttpGet("{id:int}/asignaciones")]
    public async Task<List<AsignacionDto>> Asignaciones(int id, [FromQuery] bool soloActivas = false)
    {
        if (!await _db.Mecanicos.AnyAsync(m => m.Id == id)) throw new NotFoundException("El mecánico no existe.");
        var q = _db.Asignaciones.Include(a => a.Mecanico).Include(a => a.Orden).Where(a => a.MecanicoId == id);
        if (soloActivas) q = q.Where(a => a.Activa);
        return (await q.OrderByDescending(a => a.Inicio).ToListAsync()).Select(a => a.ToDto()).ToList();
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<MecanicoDto>> Crear(MecanicoSaveDto dto)
    {
        var m = new Mecanico { Nombre = dto.Nombre.Trim(), Especialidad = dto.Especialidad, Telefono = dto.Telefono, Activo = dto.Activo };
        _db.Mecanicos.Add(m);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obtener), new { id = m.Id }, m.ToDto());
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<MecanicoDto>> Editar(int id, MecanicoSaveDto dto)
    {
        var m = await _db.Mecanicos.FindAsync(id) ?? throw new NotFoundException("El mecánico no existe.");
        if (dto.Version.HasValue) _db.Entry(m).Property(x => x.Version).OriginalValue = dto.Version.Value;
        m.Nombre = dto.Nombre.Trim();
        m.Especialidad = dto.Especialidad;
        m.Telefono = dto.Telefono;
        m.Activo = dto.Activo;
        await _db.SaveChangesAsync(); // 409 si otro lo modificó
        return m.ToDto();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var m = await _db.Mecanicos.FindAsync(id) ?? throw new NotFoundException("El mecánico no existe.");
        if (await _db.Asignaciones.AnyAsync(a => a.MecanicoId == id))
            throw new BusinessException("El mecánico tiene historial de asignaciones. Desactívelo en lugar de eliminarlo.");
        _db.Mecanicos.Remove(m);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
