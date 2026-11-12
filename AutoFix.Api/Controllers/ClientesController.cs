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

/// <summary>Módulo 2: clientes (un cliente tiene muchos vehículos).</summary>
[ApiController]
[Route("api/clientes")]
public class ClientesController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    public ClientesController(AutoFixDbContext db) => _db = db;

    [HttpGet]
    [Authorize(Policy = Politicas.Empleados)]
    public async Task<List<ClienteDto>> Listar([FromQuery] string? buscar)
    {
        var q = _db.Clientes.Include(c => c.Vehiculos).AsQueryable();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim().ToLower();
            q = q.Where(c => c.NombreCompleto.ToLower().Contains(b) || c.Dui.Contains(b) || c.Correo.ToLower().Contains(b));
        }
        var lista = await q.OrderBy(c => c.NombreCompleto).ToListAsync();
        var conUsuario = await _db.Usuarios.Where(u => u.ClienteId != null).Select(u => u.ClienteId!.Value).ToListAsync();
        return lista.Select(c => c.ToDto(conUsuario.Contains(c.Id))).ToList();
    }

    /// <summary>El cliente autenticado consulta sus propios datos y vehículos.</summary>
    [HttpGet("mio")]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<ActionResult<ClienteDto>> Mio()
    {
        var id = User.ClienteId() ?? throw new NotFoundException("Su usuario no está vinculado a un cliente.");
        return await Obtener(id);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDto>> Obtener(int id)
    {
        if (User.EsCliente() && User.ClienteId() != id)
            throw new ForbiddenException("Solo puede consultar sus propios datos.");

        var c = await _db.Clientes.Include(x => x.Vehiculos).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("El cliente no existe.");
        var tieneUsuario = await _db.Usuarios.AnyAsync(u => u.ClienteId == id);
        return c.ToDto(tieneUsuario);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<ActionResult<ClienteDto>> Crear(ClienteSaveDto dto)
    {
        if (await _db.Clientes.AnyAsync(c => c.Dui == dto.Dui.Trim()))
            throw new ConflictException($"Ya existe un cliente con el DUI {dto.Dui}.");

        var c = new Cliente();
        Copiar(dto, c);
        _db.Clientes.Add(c);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obtener), new { id = c.Id }, c.ToDto());
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<ActionResult<ClienteDto>> Editar(int id, ClienteSaveDto dto)
    {
        var c = await _db.Clientes.Include(x => x.Vehiculos).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("El cliente no existe.");
        if (await _db.Clientes.AnyAsync(x => x.Dui == dto.Dui.Trim() && x.Id != id))
            throw new ConflictException($"Ya existe otro cliente con el DUI {dto.Dui}.");
        Copiar(dto, c);
        await _db.SaveChangesAsync();
        return c.ToDto();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var c = await _db.Clientes.Include(x => x.Vehiculos).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("El cliente no existe.");
        if (await _db.Ordenes.AnyAsync(o => o.ClienteId == id))
            throw new BusinessException("El cliente tiene órdenes registradas; no se puede eliminar.");

        _db.Vehiculos.RemoveRange(c.Vehiculos);
        _db.Clientes.Remove(c);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static void Copiar(ClienteSaveDto d, Cliente c)
    {
        c.NombreCompleto = d.NombreCompleto.Trim();
        c.Dui = d.Dui.Trim();
        c.Telefono = d.Telefono.Trim();
        c.Correo = d.Correo.Trim().ToLowerInvariant();
        c.Direccion = d.Direccion?.Trim();
    }
}
