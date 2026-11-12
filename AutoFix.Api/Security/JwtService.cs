using System.Security.Claims;
using System.Text;
using AutoFix.Api.Entities;
using AutoFix.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AutoFix.Api.Security;

/// <summary>Genera el JWT con el rol (puesto) y los permisos (claims) del usuario.</summary>
public class JwtService
{
    private readonly IConfiguration _config;
    public JwtService(IConfiguration config) => _config = config;

    public AuthResponse GenerarToken(Usuario u)
    {
        var minutos = _config.GetValue("Jwt:ExpiraMinutos", 480);
        var expira = DateTime.UtcNow.AddMinutes(minutos);
        var permisos = u.ListaPermisos();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("email", u.Email),
            new("name", u.NombreCompleto),
            new("role", u.Rol)
        };
        claims.AddRange(permisos.Select(p => new Claim(Permisos.ClaimType, p)));
        if (u.ClienteId.HasValue) claims.Add(new Claim("clienteId", u.ClienteId.Value.ToString()));
        if (u.MecanicoId.HasValue) claims.Add(new Claim("mecanicoId", u.MecanicoId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            Expires = expira,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new AuthResponse
        {
            Token = token,
            Expira = expira,
            UsuarioId = u.Id,
            Email = u.Email,
            Nombre = u.NombreCompleto,
            Rol = u.Rol,
            Permisos = permisos,
            ClienteId = u.ClienteId,
            MecanicoId = u.MecanicoId
        };
    }
}
