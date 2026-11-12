using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Controllers;

/// <summary>Módulo 1: registro, inicio de sesión y perfil.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AutoFixDbContext _db;
    private readonly IPasswordHasher<Usuario> _hasher;
    private readonly JwtService _jwt;

    public AuthController(AutoFixDbContext db, IPasswordHasher<Usuario> hasher, JwtService jwt)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    /// <summary>Inicia sesión y devuelve el JWT.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest r)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);

        if (u is null || _hasher.VerifyHashedPassword(u, u.PasswordHash, r.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new ApiError
            {
                Status = 401, Error = "No autenticado", Mensaje = "Correo o contraseña incorrectos.",
                CorrelationId = HttpContext.TraceIdentifier
            });

        if (!u.Activo)
            throw new ForbiddenException("El usuario está desactivado.");

        return Ok(_jwt.GenerarToken(u));
    }

    /// <summary>Registro público de clientes (rol Cliente). Si recepción ya registró su DUI, se vincula.</summary>
    [AllowAnonymous]
    [HttpPost("registro")]
    public async Task<ActionResult<AuthResponse>> Registro(RegistroRequest r)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (await _db.Usuarios.AnyAsync(x => x.Email == email))
            throw new ConflictException("Ya existe un usuario con ese correo.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Dui == r.Dui.Trim());
        if (cliente != null)
        {
            if (await _db.Usuarios.AnyAsync(x => x.ClienteId == cliente.Id))
                throw new ConflictException("Ese DUI ya tiene una cuenta registrada.");
        }
        else
        {
            cliente = new Cliente
            {
                NombreCompleto = r.NombreCompleto.Trim(), Dui = r.Dui.Trim(), Telefono = r.Telefono.Trim(),
                Correo = email, Direccion = r.Direccion
            };
            _db.Clientes.Add(cliente);
            await _db.SaveChangesAsync();
        }

        var u = new Usuario
        {
            NombreCompleto = r.NombreCompleto.Trim(), Email = email, Rol = Roles.Cliente,
            Permisos = "", ClienteId = cliente.Id
        };
        u.PasswordHash = _hasher.HashPassword(u, r.Password);
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(_jwt.GenerarToken(u));
    }

    /// <summary>Datos del usuario autenticado (útil para probar el token en Swagger).</summary>
    [HttpGet("perfil")]
    public async Task<ActionResult<UsuarioDto>> Perfil()
    {
        var u = await _db.Usuarios.FindAsync(User.UsuarioId()) ?? throw new NotFoundException("Usuario no encontrado.");
        return u.ToDto();
    }
}
