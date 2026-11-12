using System.Globalization;
using AutoFix.Mvc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(o => o.Filters.Add<ApiExceptionFilter>());
builder.Services.AddHttpContextAccessor();

// ───────────── Consumo de la API con HttpClient ─────────────
// JwtHandler agrega "Authorization: Bearer <token>" y el X-Correlation-ID a CADA petición.
builder.Services.AddTransient<JwtHandler>();
builder.Services.AddHttpClient("AutoFixApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(60);
}).AddHttpMessageHandler<JwtHandler>();
builder.Services.AddScoped<ApiClient>();

// ───────────── Sesión del usuario en el MVC ─────────────
// La cookie (cifrada) guarda el JWT y los datos del usuario (rol y permisos) para mostrar/ocultar opciones.
// OJO: ocultar un botón no es seguridad; la seguridad real la aplica la API.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/AccesoDenegado";
        o.Cookie.Name = "AutoFix.Sesion";
        o.Cookie.HttpOnly = true;
        o.SlidingExpiration = false;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

Ui.ApiBase = builder.Configuration["Api:BaseUrl"]!;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Cultura fija para que los decimales de los formularios usen punto (45.50) sin importar la configuración de Windows.
var cultura = new CultureInfo("en-US");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(cultura),
    SupportedCultures = new List<CultureInfo> { cultura },
    SupportedUICultures = new List<CultureInfo> { cultura }
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
