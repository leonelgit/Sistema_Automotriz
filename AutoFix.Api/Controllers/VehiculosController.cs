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

/// <summary>Módulo 2: vehículos (pertenecen a un cliente) + fotografía del vehículo.</summary>
[ApiController]
[Route("api/vehiculos")]
public class VehiculosController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly ImagenService _imagenes;

    public VehiculosController(AutoFixDbContext db, ImagenService imagenes)
    {
        _db = db;
        _imagenes = imagenes;
    }

    /// <summary>Empleados ven todos; un Cliente solo ve los suyos.</summary>
    [HttpGet]
    public async Task<List<VehiculoDto>> Listar([FromQuery] int? clienteId, [FromQuery] string? buscar)
    {
        var q = _db.Vehiculos.Include(v => v.Cliente).AsQueryable();
        if (User.EsCliente())
        {
            var miId = User.ClienteId();
            q = q.Where(v => v.ClienteId == miId);
        }
        else if (clienteId.HasValue) q = q.Where(v => v.ClienteId == clienteId);

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim().ToLower();
            q = q.Where(v => v.Placa.ToLower().Contains(b) || v.Marca.ToLower().Contains(b) || v.Modelo.ToLower().Contains(b));
        }
        return (await q.OrderBy(v => v.Placa).ToListAsync()).Select(v => v.ToDto()).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VehiculoDto>> Obtener(int id)
    {
        var v = await _db.Vehiculos.Include(x => x.Cliente).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("El vehículo no existe.");
        if (User.EsCliente() && v.ClienteId != User.ClienteId())
            throw new ForbiddenException("Solo puede consultar sus propios vehículos.");
        return v.ToDto();
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<ActionResult<VehiculoDto>> Crear(VehiculoSaveDto dto)
    {
        if (!await _db.Clientes.AnyAsync(c => c.Id == dto.ClienteId))
            throw new NotFoundException("El cliente propietario no existe.");
        await ValidarUnicos(dto, 0);

        var v = new Vehiculo();
        Copiar(dto, v);
        _db.Vehiculos.Add(v);
        await _db.SaveChangesAsync();
        await _db.Entry(v).Reference(x => x.Cliente).LoadAsync();
        return CreatedAtAction(nameof(Obtener), new { id = v.Id }, v.ToDto());
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    public async Task<ActionResult<VehiculoDto>> Editar(int id, VehiculoSaveDto dto)
    {
        var v = await _db.Vehiculos.Include(x => x.Cliente).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("El vehículo no existe.");
        if (!await _db.Clientes.AnyAsync(c => c.Id == dto.ClienteId))
            throw new NotFoundException("El cliente propietario no existe.");
        await ValidarUnicos(dto, id);
        Copiar(dto, v);
        await _db.SaveChangesAsync();
        await _db.Entry(v).Reference(x => x.Cliente).LoadAsync();
        return v.ToDto();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var v = await _db.Vehiculos.FindAsync(id) ?? throw new NotFoundException("El vehículo no existe.");
        if (await _db.Ordenes.AnyAsync(o => o.VehiculoId == id))
            throw new BusinessException("El vehículo tiene órdenes registradas; no se puede eliminar.");
        _imagenes.Eliminar(v.FotoRuta);
        _db.Vehiculos.Remove(v);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Sube/reemplaza la fotografía del vehículo (multipart/form-data, JPG/PNG, máx. 5 MB).</summary>
    [HttpPost("{id:int}/foto")]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Recepcionista)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ImagenSubidaDto>> SubirFoto(int id, IFormFile archivo)
    {
        var v = await _db.Vehiculos.FindAsync(id) ?? throw new NotFoundException("El vehículo no existe.");
        var ruta = await _imagenes.GuardarAsync(archivo, "vehiculos");
        var anterior = v.FotoRuta;
        v.FotoRuta = ruta;
        await _db.SaveChangesAsync();
        _imagenes.Eliminar(anterior);
        return Ok(new ImagenSubidaDto { Ruta = ruta });
    }

    private async Task ValidarUnicos(VehiculoSaveDto dto, int id)
    {
        var placa = dto.Placa.Trim().ToUpperInvariant();
        var chasis = dto.NumeroChasis.Trim().ToUpperInvariant();
        if (await _db.Vehiculos.AnyAsync(v => v.Placa == placa && v.Id != id))
            throw new ConflictException($"Ya existe un vehículo con la placa {placa}.");
        if (await _db.Vehiculos.AnyAsync(v => v.NumeroChasis == chasis && v.Id != id))
            throw new ConflictException($"Ya existe un vehículo con el chasis {chasis}.");
    }

    private static void Copiar(VehiculoSaveDto d, Vehiculo v)
    {
        v.Placa = d.Placa.Trim().ToUpperInvariant();
        v.Marca = d.Marca.Trim();
        v.Modelo = d.Modelo.Trim();
        v.Anio = d.Anio;
        v.Color = d.Color.Trim();
        v.NumeroChasis = d.NumeroChasis.Trim().ToUpperInvariant();
        v.ClienteId = d.ClienteId;
    }
}
