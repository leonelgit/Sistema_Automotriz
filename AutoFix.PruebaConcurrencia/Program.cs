// ─────────────────────────────────────────────────────────────────────────────
//  AutoFix · Prueba de concurrencia (módulo 6)
//  Uso:  dotnet run              (usa http://localhost:5080/)
//        dotnet run -- http://localhost:5080/
//  La API debe estar ejecutándose.
// ─────────────────────────────────────────────────────────────────────────────
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = args.FirstOrDefault() ?? "http://localhost:5080/";
var sufijo = DateTime.Now.ToString("HHmmss");

Console.OutputEncoding = System.Text.Encoding.UTF8;
Titulo("AutoFix · Prueba de concurrencia");
Console.WriteLine($"API: {baseUrl}\n");

var admin = await Login("admin@autofix.com", "Admin123!");
var mecanico1 = await Login("mecanico@autofix.com", "Mecanico123!");
var mecanico2 = await Login("mecanico2@autofix.com", "Mecanico123!");
var supervisor1 = await Login("supervisor@autofix.com", "Supervisor123!");
var supervisor2 = await Login("supervisor2@autofix.com", "Supervisor123!");

// ═════════════════════ ESCENARIO A: INVENTARIO ═════════════════════
Titulo("ESCENARIO A · Dos mecánicos piden 2 unidades cuando solo existen 2");

var repuesto = await Leer(await admin.PostAsJsonAsync("api/repuestos", new
{
    codigo = $"CONC-{sufijo}", nombre = "Pastillas de freno (prueba de concurrencia)", categoria = "Frenos",
    marca = "Prueba", precio = 45.0m, existencias = 2, stockMinimo = 1
}));
var repuestoId = repuesto.GetProperty("id").GetInt32();
var version = repuesto.GetProperty("version").GetString();
Console.WriteLine($"Repuesto creado id={repuestoId} con 2 existencias. Versión leída por ambos mecánicos: {version}\n");

// Ambos mecánicos leyeron la MISMA versión y envían su salida al mismo tiempo.
var tareasA = new[]
{
    Enviar(mecanico1, $"api/repuestos/{repuestoId}/salidas", new { cantidad = 2, motivo = "Mecánico 1 - orden X", version }, $"conc-A1-{sufijo}"),
    Enviar(mecanico2, $"api/repuestos/{repuestoId}/salidas", new { cantidad = 2, motivo = "Mecánico 2 - orden Y", version }, $"conc-A2-{sufijo}")
};
var resultadosA = await Task.WhenAll(tareasA);
Mostrar("Mecánico 1", resultadosA[0]);
Mostrar("Mecánico 2", resultadosA[1]);

var final = await Leer(await admin.GetAsync($"api/repuestos/{repuestoId}"));
Console.WriteLine($"\n➜ Existencias finales: {final.GetProperty("existencias").GetInt32()} (nunca negativas; solo se aplicó una salida)");

// Variante sin enviar versión (carrera "real" en el servidor).
Console.WriteLine("\nVariante: sin enviar versión (los dos leen a la vez en el servidor)");
await Enviar(admin, $"api/repuestos/{repuestoId}/entradas", new { cantidad = 2, motivo = "Reposición para variante" }, null);
var tareasA2 = new[]
{
    Enviar(mecanico1, $"api/repuestos/{repuestoId}/salidas", new { cantidad = 2, motivo = "Variante M1" }, $"conc-A3-{sufijo}"),
    Enviar(mecanico2, $"api/repuestos/{repuestoId}/salidas", new { cantidad = 2, motivo = "Variante M2" }, $"conc-A4-{sufijo}")
};
var resultadosA2 = await Task.WhenAll(tareasA2);
Mostrar("Mecánico 1", resultadosA2[0]);
Mostrar("Mecánico 2", resultadosA2[1]);
final = await Leer(await admin.GetAsync($"api/repuestos/{repuestoId}"));
Console.WriteLine($"➜ Existencias finales: {final.GetProperty("existencias").GetInt32()} (una gana; la otra recibe 409 o 400 por existencias insuficientes)");

// ═════════════════════ ESCENARIO B: ASIGNACIÓN ═════════════════════
Titulo("ESCENARIO B · Dos supervisores asignan al mismo mecánico en horarios que se cruzan");

var vehiculos = await Leer(await admin.GetAsync("api/vehiculos"));
var listaVeh = vehiculos.EnumerateArray().ToList();
if (listaVeh.Count == 0) { Console.WriteLine("No hay vehículos. Registre al menos uno."); return; }
var vehiculoId = listaVeh[0].GetProperty("id").GetInt32();
var fechaFin = DateTime.Now.AddDays(5);

var orden1 = await Leer(await admin.PostAsJsonAsync("api/ordenes", new
{
    vehiculoId, tipoServicio = $"Prueba concurrencia {sufijo} A", fechaEstimadaFin = fechaFin, descripcionProblema = "Orden de prueba 1"
}));
var orden2 = await Leer(await admin.PostAsJsonAsync("api/ordenes", new
{
    vehiculoId, tipoServicio = $"Prueba concurrencia {sufijo} B", fechaEstimadaFin = fechaFin, descripcionProblema = "Orden de prueba 2"
}));
var o1 = orden1.GetProperty("id").GetInt32();
var o2 = orden2.GetProperty("id").GetInt32();
Console.WriteLine($"Órdenes creadas: {orden1.GetProperty("numeroOrden").GetString()} y {orden2.GetProperty("numeroOrden").GetString()}");

var mecanicos = await Leer(await admin.GetAsync("api/mecanicos?soloActivos=true"));
var mec = mecanicos.EnumerateArray().First();
var mecId = mec.GetProperty("id").GetInt32();
var mecVersion = mec.GetProperty("version").GetString();
Console.WriteLine($"Mecánico: {mec.GetProperty("nombre").GetString()} (versión leída: {mecVersion})");

// Horario aleatorio en el futuro para que no choque con pruebas anteriores
var dia = DateTime.Today.AddDays(30 + Random.Shared.Next(0, 300)).AddHours(8);
var tareasB = new[]
{
    Enviar(supervisor1, $"api/ordenes/{o1}/asignar", new { mecanicoId = mecId, inicio = dia, fin = dia.AddHours(4), mecanicoVersion = mecVersion }, $"conc-B1-{sufijo}"),
    Enviar(supervisor2, $"api/ordenes/{o2}/asignar", new { mecanicoId = mecId, inicio = dia.AddHours(2), fin = dia.AddHours(6), mecanicoVersion = mecVersion }, $"conc-B2-{sufijo}")
};
var resultadosB = await Task.WhenAll(tareasB);
Mostrar($"Supervisor 1 → orden {o1} ({dia:dd/MM HH:mm}-{dia.AddHours(4):HH:mm})", resultadosB[0]);
Mostrar($"Supervisor 2 → orden {o2} ({dia.AddHours(2):dd/MM HH:mm}-{dia.AddHours(6):HH:mm})", resultadosB[1]);

// ═════════════════════ LOGS ═════════════════════
Titulo("Logs de las operaciones rechazadas (por Correlation ID)");
await Task.Delay(1500); // el log se escribe en segundo plano
foreach (var r in resultadosA.Concat(resultadosA2).Concat(resultadosB).Where(r => r.Status >= 400))
{
    var logs = await Leer(await admin.GetAsync($"api/logs?correlationId={r.CorrelationId}"));
    foreach (var l in logs.EnumerateArray())
        Console.WriteLine($"{l.GetProperty("fecha").GetDateTime():yyyy-MM-dd HH:mm:ss} | {l.GetProperty("codigo").GetInt32()} | " +
                          $"{l.GetProperty("metodo").GetString()} {l.GetProperty("endpoint").GetString()} | {l.GetProperty("usuario").GetString()} | " +
                          $"{l.GetProperty("duracionMs").GetInt64()} ms | CorrelationId={l.GetProperty("correlationId").GetString()}");
}

// Limpieza: cancelar las órdenes de prueba para liberar la agenda
await Enviar(admin, $"api/ordenes/{o1}/cancelar", new { comentario = "Fin de prueba de concurrencia" }, null);
await Enviar(admin, $"api/ordenes/{o2}/cancelar", new { comentario = "Fin de prueba de concurrencia" }, null);
Console.WriteLine("\nÓrdenes de prueba canceladas. Fin.");

// ───────────────────────── utilidades ─────────────────────────
async Task<HttpClient> Login(string email, string password)
{
    var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var resp = await http.PostAsJsonAsync("api/auth/login", new { email, password });
    var json = await Leer(resp);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
    Console.WriteLine($"✔ Sesión iniciada: {email}");
    return http;
}

async Task<Resultado> Enviar(HttpClient http, string url, object body, string? correlationId)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    if (correlationId != null) req.Headers.Add("X-Correlation-ID", correlationId);
    using var resp = await http.SendAsync(req);
    var texto = await resp.Content.ReadAsStringAsync();
    return new Resultado((int)resp.StatusCode, texto,
        resp.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.First() : correlationId ?? "");
}

static async Task<JsonElement> Leer(HttpResponseMessage resp)
{
    var texto = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
        throw new Exception($"La API respondió {(int)resp.StatusCode}: {texto}");
    return JsonDocument.Parse(texto).RootElement.Clone();
}

static void Mostrar(string quien, Resultado r)
{
    var color = r.Status < 300 ? ConsoleColor.Green : r.Status == 409 ? ConsoleColor.Yellow : ConsoleColor.Red;
    Console.ForegroundColor = color;
    Console.Write($"  {quien}: HTTP {r.Status}");
    Console.ResetColor();
    string detalle;
    try
    {
        var json = JsonDocument.Parse(r.Cuerpo).RootElement;
        detalle = json.TryGetProperty("mensaje", out var m) ? m.GetString() ?? "" : "OK";
    }
    catch { detalle = r.Cuerpo; }
    Console.WriteLine($"  →  {detalle}  [CorrelationId: {r.CorrelationId}]");
}

static void Titulo(string t)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n══════ {t} ══════");
    Console.ResetColor();
}

record Resultado(int Status, string Cuerpo, string CorrelationId);
