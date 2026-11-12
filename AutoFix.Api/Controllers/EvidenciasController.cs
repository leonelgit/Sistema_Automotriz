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

/// <summary>Formulario multipart para subir evidencias.</summary>
public class SubirEvidenciaForm
{
    public IFormFile? Archivo { get; set; }
    public TipoEvidencia Tipo { get; set; } = TipoEvidencia.Danio;
    public string? Descripcion { get; set; }
}

/// <summary>
/// Módulo 7: fotografías de ingreso, daños, repuestos utilizados y reparación finalizada.
/// La imagen se guarda en wwwroot/uploads/evidencias y en SQLite solo la ruta.
/// </summary>
[ApiController]
public class EvidenciasController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly ImagenService _imagenes;

    public EvidenciasController(AutoFixDbContext db, ImagenService imagenes)
    {
        _db = db;
        _imagenes = imagenes;
    }

    [HttpPost("api/ordenes/{ordenId:int}/evidencias")]
    [Authorize(Policy = Politicas.PuedeEditarOrdenes)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<EvidenciaDto>> Subir(int ordenId, [FromForm] SubirEvidenciaForm form)
    {
        var orden = await _db.Ordenes.FindAsync(ordenId) ?? throw new NotFoundException("La orden no existe.");
        OrdenService.VerificarAcceso(orden, User, paraModificar: true);
        if (orden.Estado == EstadoOrden.Cancelado)
            throw new BusinessException("No se pueden agregar evidencias a una orden cancelada.");

        var ruta = await _imagenes.GuardarAsync(form.Archivo, "evidencias");

        var evidencia = new Evidencia
        {
            OrdenId = ordenId, Tipo = form.Tipo, Ruta = ruta, Descripcion = form.Descripcion?.Trim(),
            NombreOriginal = Path.GetFileName(form.Archivo!.FileName), TamanoBytes = form.Archivo.Length,
            SubidoPor = User.Email(), Fecha = DateTime.Now
        };
        _db.Evidencias.Add(evidencia);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _imagenes.Eliminar(ruta); // si falla la base, no dejamos archivos huérfanos
            throw;
        }
        return Ok(evidencia.ToDto(orden.NumeroOrden));
    }

    [HttpGet("api/ordenes/{ordenId:int}/evidencias")]
    public async Task<ActionResult<List<EvidenciaDto>>> PorOrden(int ordenId)
    {
        var orden = await _db.Ordenes.FindAsync(ordenId) ?? throw new NotFoundException("La orden no existe.");
        OrdenService.VerificarAcceso(orden, User);
        var lista = await _db.Evidencias.Where(e => e.OrdenId == ordenId).OrderByDescending(e => e.Fecha).ToListAsync();
        return lista.Select(e => e.ToDto(orden.NumeroOrden)).ToList();
    }

    /// <summary>Galería general (empleados).</summary>
    [HttpGet("api/evidencias")]
    [Authorize(Policy = Politicas.Empleados)]
    public async Task<List<EvidenciaDto>> Listar([FromQuery] TipoEvidencia? tipo, [FromQuery] int? ordenId, [FromQuery] int take = 100)
    {
        var q = _db.Evidencias.Include(e => e.Orden).AsQueryable();
        if (User.EsMecanico())
        {
            var miMecanico = User.MecanicoId();
            q = q.Where(e => e.Orden.MecanicoId == miMecanico);
        }
        if (tipo.HasValue) q = q.Where(e => e.Tipo == tipo);
        if (ordenId.HasValue) q = q.Where(e => e.OrdenId == ordenId);
        take = Math.Clamp(take, 1, 500);
        return (await q.OrderByDescending(e => e.Id).Take(take).ToListAsync()).Select(e => e.ToDto()).ToList();
    }

    [HttpDelete("api/evidencias/{id:int}")]
    [Authorize(Roles = Roles.Administrador + "," + Roles.Supervisor)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var e = await _db.Evidencias.FindAsync(id) ?? throw new NotFoundException("La evidencia no existe.");
        _db.Evidencias.Remove(e);
        await _db.SaveChangesAsync();
        _imagenes.Eliminar(e.Ruta);
        return NoContent();
    }
}
