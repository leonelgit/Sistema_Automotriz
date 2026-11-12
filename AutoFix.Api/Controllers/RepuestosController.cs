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

/// <summary>Módulo 5: inventario de repuestos. Escritura con claim Inventario.Gestionar.</summary>
[ApiController]
[Route("api/repuestos")]
[Authorize(Policy = Politicas.Empleados)]
public class RepuestosController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly InventarioService _inventario;
    private readonly ImagenService _imagenes;

    public RepuestosController(AutoFixDbContext db, InventarioService inventario, ImagenService imagenes)
    {
        _db = db;
        _inventario = inventario;
        _imagenes = imagenes;
    }

    /// <summary>Consultar existencias.</summary>
    [HttpGet]
    public async Task<List<RepuestoDto>> Listar([FromQuery] string? buscar, [FromQuery] string? categoria, [FromQuery] bool soloBajoStock = false)
    {
        var q = _db.Repuestos.Where(r => r.Activo);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim().ToLower();
            q = q.Where(r => r.Codigo.ToLower().Contains(b) || r.Nombre.ToLower().Contains(b) || r.Marca.ToLower().Contains(b));
        }
        if (!string.IsNullOrWhiteSpace(categoria)) q = q.Where(r => r.Categoria == categoria);
        if (soloBajoStock) q = q.Where(r => r.Existencias <= r.StockMinimo);
        return (await q.OrderBy(r => r.Nombre).ToListAsync()).Select(r => r.ToDto()).ToList();
    }

    /// <summary>Alerta: repuestos que llegaron al stock mínimo.</summary>
    [HttpGet("alertas")]
    public async Task<List<RepuestoDto>> Alertas() =>
        (await _db.Repuestos.Where(r => r.Activo && r.Existencias <= r.StockMinimo).OrderBy(r => r.Existencias).ToListAsync())
        .Select(r => r.ToDto()).ToList();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RepuestoDto>> Obtener(int id) =>
        (await _db.Repuestos.FindAsync(id) ?? throw new NotFoundException("El repuesto no existe.")).ToDto();

    [HttpPost]
    [Authorize(Policy = Politicas.PuedeGestionarInventario)]
    public async Task<ActionResult<RepuestoDto>> Crear(RepuestoSaveDto dto)
    {
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        if (await _db.Repuestos.AnyAsync(r => r.Codigo == codigo))
            throw new ConflictException($"Ya existe un repuesto con el código {codigo}.");

        var r = new Repuesto { Codigo = codigo, Existencias = dto.Existencias };
        Copiar(dto, r);
        _db.Repuestos.Add(r);

        if (dto.Existencias > 0)
            _db.Movimientos.Add(new MovimientoInventario
            {
                Repuesto = r, Tipo = TipoMovimiento.Entrada, Cantidad = dto.Existencias, ExistenciaAnterior = 0,
                ExistenciaResultante = dto.Existencias, Motivo = "Inventario inicial", Usuario = User.Email()
            });

        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obtener), new { id = r.Id }, r.ToDto());
    }

    /// <summary>Modificar información (no las existencias: esas cambian solo con entradas/salidas).</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Politicas.PuedeGestionarInventario)]
    public async Task<ActionResult<RepuestoDto>> Editar(int id, RepuestoSaveDto dto)
    {
        var r = await _db.Repuestos.FindAsync(id) ?? throw new NotFoundException("El repuesto no existe.");
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        if (await _db.Repuestos.AnyAsync(x => x.Codigo == codigo && x.Id != id))
            throw new ConflictException($"Ya existe otro repuesto con el código {codigo}.");

        if (dto.Version.HasValue) _db.Entry(r).Property(x => x.Version).OriginalValue = dto.Version.Value;
        r.Codigo = codigo;
        Copiar(dto, r);
        await _db.SaveChangesAsync(); // DbUpdateConcurrencyException => 409
        return r.ToDto();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Politicas.PuedeGestionarInventario)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await _db.Repuestos.FindAsync(id) ?? throw new NotFoundException("El repuesto no existe.");
        var usado = await _db.Movimientos.AnyAsync(m => m.RepuestoId == id && m.Tipo == TipoMovimiento.Salida)
                    || await _db.DetallesPresupuesto.AnyAsync(d => d.RepuestoId == id);
        if (usado) r.Activo = false; // baja lógica para conservar el historial
        else
        {
            _db.Movimientos.RemoveRange(_db.Movimientos.Where(m => m.RepuestoId == id));
            _db.Repuestos.Remove(r);
            _imagenes.Eliminar(r.FotoRuta);
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Registrar entrada de inventario.</summary>
    [HttpPost("{id:int}/entradas")]
    [Authorize(Policy = Politicas.PuedeGestionarInventario)]
    public async Task<ActionResult<MovimientoResultadoDto>> Entrada(int id, MovimientoRequest r) =>
        await _inventario.RegistrarEntradaAsync(id, r, User.Email());

    /// <summary>
    /// Registrar salida (uso de repuesto). Escenario A: si dos mecánicos piden las mismas unidades a la vez,
    /// solo una salida se aplica y la otra recibe 409 (o 400 si ya no hay existencias).
    /// </summary>
    [HttpPost("{id:int}/salidas")]
    [Authorize(Policy = Politicas.PuedeUsarRepuestos)]
    public async Task<ActionResult<MovimientoResultadoDto>> Salida(int id, MovimientoRequest r) =>
        await _inventario.RegistrarSalidaAsync(id, r, User.Email());

    /// <summary>Movimientos de un repuesto.</summary>
    [HttpGet("{id:int}/movimientos")]
    public async Task<List<MovimientoDto>> Movimientos(int id) =>
        (await _db.Movimientos.Include(m => m.Repuesto).Include(m => m.Orden)
            .Where(m => m.RepuestoId == id).OrderByDescending(m => m.Id).ToListAsync())
        .Select(m => m.ToDto()).ToList();

    [HttpPost("{id:int}/foto")]
    [Authorize(Policy = Politicas.PuedeGestionarInventario)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ImagenSubidaDto>> SubirFoto(int id, IFormFile archivo)
    {
        var r = await _db.Repuestos.FindAsync(id) ?? throw new NotFoundException("El repuesto no existe.");
        var ruta = await _imagenes.GuardarAsync(archivo, "repuestos");
        var anterior = r.FotoRuta;
        r.FotoRuta = ruta;
        await _db.SaveChangesAsync();
        _imagenes.Eliminar(anterior);
        return Ok(new ImagenSubidaDto { Ruta = ruta });
    }

    private static void Copiar(RepuestoSaveDto d, Repuesto r)
    {
        r.Nombre = d.Nombre.Trim();
        r.Descripcion = d.Descripcion?.Trim();
        r.Categoria = d.Categoria.Trim();
        r.Marca = d.Marca.Trim();
        r.Precio = Math.Round(d.Precio, 2);
        r.StockMinimo = d.StockMinimo;
    }
}
