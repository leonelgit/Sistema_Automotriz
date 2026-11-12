using System.Text;
using System.Text.Json.Serialization;
using AutoFix.Api.Data;
using AutoFix.Api.Entities;
using AutoFix.Api.Logging;
using AutoFix.Api.Middlewares;
using AutoFix.Api.Security;
using AutoFix.Api.Services;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ───────────── Base de datos: EF Core + SQLite ─────────────
// La ruta del archivo .db se resuelve respecto a la carpeta del proyecto, así funciona igual
// con "dotnet run", "dotnet ef" o el depurador de VS Code.
var csb = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=autofix.db;Default Timeout=30");
if (!Path.IsPathRooted(csb.DataSource))
    csb.DataSource = Path.Combine(builder.Environment.ContentRootPath, csb.DataSource);

builder.Services.AddDbContext<AutoFixDbContext>(opt => opt.UseSqlite(csb.ToString()));

// ───────────── Servicios de negocio ─────────────
builder.Services.AddScoped<InventarioService>();
builder.Services.AddScoped<OrdenService>();
builder.Services.AddScoped<PresupuestoService>();
builder.Services.AddScoped<ImagenService>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

// Logs: cola en memoria + escritor en segundo plano (archivo + tabla LogsSolicitudes)
builder.Services.AddSingleton<LogQueue>();
builder.Services.AddHostedService<LogWriterService>();

// ───────────── Controladores ─────────────
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o =>
    {
        // Errores de validación con el mismo formato que el resto de errores (ApiError).
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var errores = ctx.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(er =>
                    string.IsNullOrEmpty(er.ErrorMessage) ? $"{e.Key}: valor inválido" : er.ErrorMessage))
                .ToList();
            return new BadRequestObjectResult(new ApiError
            {
                Status = 400, Error = "Datos inválidos", Mensaje = "Revise los datos enviados.",
                CorrelationId = ctx.HttpContext.CorrelationId(), Errores = errores
            });
        };
    });

// ───────────── Autenticación JWT ─────────────
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException(
        "Configure 'Jwt:Key' (mínimo 32 caracteres) con user-secrets, variable de entorno Jwt__Key o appsettings.Development.json.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // conservar los nombres cortos: email, role, permiso...
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "email",
            RoleClaimType = "role"
        };
        o.Events = new JwtBearerEvents
        {
            // 401 con cuerpo JSON claro (sin token o token inválido/expirado)
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new ApiError
                {
                    Status = 401, Error = "No autenticado",
                    Mensaje = string.IsNullOrEmpty(ctx.Request.Headers.Authorization)
                        ? "Debe enviar un token JWT (Authorization: Bearer <token>)."
                        : "El token JWT es inválido o expiró. Inicie sesión nuevamente.",
                    CorrelationId = ctx.HttpContext.CorrelationId()
                });
            },
            // 403 con cuerpo JSON (autenticado pero sin el rol/claim requerido)
            OnForbidden = async ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new ApiError
                {
                    Status = 403, Error = "Prohibido",
                    Mensaje = "No tiene el rol o el permiso (claim) necesario para esta acción.",
                    CorrelationId = ctx.HttpContext.CorrelationId()
                });
            }
        };
    });

builder.Services.AddAuthorization(Politicas.Registrar);

// ───────────── Swagger con soporte para JWT ─────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AutoFix API",
        Version = "v1",
        Description = "Gestión de órdenes de reparación automotriz. Use POST /api/auth/login y luego el botón Authorize."
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pegue solo el token (sin la palabra Bearer)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ───────────── Migraciones + datos iniciales ─────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AutoFixDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (!db.Database.GetMigrations().Any())
    {
        throw new InvalidOperationException(
            "No hay migraciones. Ejecute en la carpeta AutoFix.Api:  dotnet ef migrations add InicialAutoFix  y luego  dotnet ef database update");
    }

    db.Database.Migrate();
    // WAL permite leer mientras otro escribe (mejor para peticiones simultáneas).
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    await DataSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>(), logger);
    logger.LogInformation("Base de datos lista en {Ruta}", csb.DataSource);
}

// ───────────── Pipeline (el ORDEN importa) ─────────────
// 1) Correlation ID -> 2) Excepciones -> 3) Registro de solicitudes -> 4) Autenticación -> 5) Autorización -> 6) Controladores
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseStaticFiles(); // sirve /uploads/... (las imágenes guardadas por la API)

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AutoFix API v1");
    c.DocumentTitle = "AutoFix API";
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous().ExcludeFromDescription();
app.MapControllers();

app.Run();

public partial class Program { }
