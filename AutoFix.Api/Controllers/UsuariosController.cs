using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Administración de usuarios internos, roles y permisos (solo Administrador).</summary>
[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = Roles.Administrador)]
public class UsuariosController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly IPasswordHasher<Usuario> _hasher;

    public UsuariosController(AutoFixDbContext db, IPasswordHasher<Usuario> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    [HttpGet]
    public async Task<List<UsuarioDto>> Listar() =>
        (await _db.Usuarios.OrderBy(u => u.Rol).ThenBy(u => u.Email).ToListAsync()).Select(u => u.ToDto()).ToList();

    [HttpPost]
    public async Task<ActionResult<UsuarioDto>> Crear(CrearUsuarioRequest r)
    {
        if (!Roles.Todos.Contains(r.Rol)) throw new BusinessException("Rol no válido.");
        var email = r.Email.Trim().ToLowerInvariant();
        if (await _db.Usuarios.AnyAsync(u => u.Email == email)) throw new ConflictException("Ya existe un usuario con ese correo.");

        var permisos = ValidarPermisos(r.Permisos ?? Permisos.PorDefecto(r.Rol).ToList());

        if (r.Rol == Roles.Mecanico && r.MecanicoId is null)
        {
            // Si no se vincula a un mecánico existente, se crea uno nuevo con el mismo nombre.
            var m = new Mecanico { Nombre = r.NombreCompleto.Trim() };
            _db.Mecanicos.Add(m);
            await _db.SaveChangesAsync();
            r.MecanicoId = m.Id;
        }
        if (r.Rol == Roles.Cliente && r.ClienteId is null)
            throw new BusinessException("Para un usuario Cliente indique el ClienteId (o use el registro público).");

        var u = new Usuario
        {
            NombreCompleto = r.NombreCompleto.Trim(), Email = email, Rol = r.Rol,
            Permisos = string.Join(',', permisos),
            MecanicoId = r.Rol == Roles.Mecanico ? r.MecanicoId : null,
            ClienteId = r.Rol == Roles.Cliente ? r.ClienteId : null
        };
        u.PasswordHash = _hasher.HashPassword(u, r.Password);
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Listar), new { id = u.Id }, u.ToDto());
    }

    /// <summary>Cambia los permisos (claims) de un usuario. Tendrá efecto en su próximo login.</summary>
    [HttpPut("{id:int}/permisos")]
    public async Task<ActionResult<UsuarioDto>> ActualizarPermisos(int id, ActualizarPermisosRequest r)
    {
        var u = await _db.Usuarios.FindAsync(id) ?? throw new NotFoundException("Usuario no encontrado.");
        u.Permisos = string.Join(',', ValidarPermisos(r.Permisos));
        await _db.SaveChangesAsync();
        return u.ToDto();
    }

    [HttpPut("{id:int}/activo")]
    public async Task<ActionResult<UsuarioDto>> CambiarActivo(int id, [FromQuery] bool activo)
    {
        var u = await _db.Usuarios.FindAsync(id) ?? throw new NotFoundException("Usuario no encontrado.");
        u.Activo = activo;
        await _db.SaveChangesAsync();
        return u.ToDto();
    }

    private static List<string> ValidarPermisos(IEnumerable<string> permisos)
    {
        var lista = permisos.Select(p => p.Trim()).Where(p => p != "").Distinct().ToList();
        var invalidos = lista.Where(p => !Permisos.Todos.Contains(p)).ToList();
        if (invalidos.Count > 0) throw new BusinessException("Permisos no válidos: " + string.Join(", ", invalidos));
        return lista;
    }
}
