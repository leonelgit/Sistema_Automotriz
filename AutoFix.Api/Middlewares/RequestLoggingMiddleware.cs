using System.Diagnostics;
using AutoFix.Api.Entities;
using AutoFix.Api.Exceptions;
using AutoFix.Api.Logging;

namespace AutoFix.Api.Middlewares;

/// <summary>
/// (3) Registro de solicitudes HTTP: un log por petición con usuario, método, endpoint,
/// fecha, código, duración, Correlation ID y la excepción (si ocurrió).
/// Se ejecuta DESPUÉS de CorrelationId y Excepciones, por eso ya conoce el Correlation ID
/// y ve la excepción antes de que el middleware de excepciones la convierta en respuesta.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly LogQueue _queue;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, LogQueue queue)
    {
        _next = next;
        _logger = logger;
        _queue = queue;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Solo registramos la API (no imágenes estáticas ni Swagger).
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        Exception? error = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            error = ex;
            throw; // el middleware de excepciones (más afuera) construye la respuesta
        }
        finally
        {
            sw.Stop();
            var status = error is null ? context.Response.StatusCode : ExceptionMapper.Map(error).Status;
            // HttpContext.User ya fue llenado por el middleware de autenticación (que corre después).
            var usuario = context.User?.FindFirst("email")?.Value ?? "anónimo";

            var log = new LogSolicitud
            {
                Fecha = DateTime.Now,
                Usuario = usuario,
                Metodo = context.Request.Method,
                Endpoint = context.Request.Path + context.Request.QueryString,
                Codigo = status,
                DuracionMs = sw.ElapsedMilliseconds,
                CorrelationId = context.CorrelationId(),
                Ip = context.Connection.RemoteIpAddress?.ToString(),
                Excepcion = error is null ? null : $"{error.GetType().Name}: {error.Message}",
                Detalle = error is null ? null : Recortar(error.ToString(), 4000)
            };

            var nivel = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            _logger.Log(nivel,
                "HTTP {Metodo} {Endpoint} => {Codigo} en {Duracion} ms | Usuario={Usuario} | CorrelationId={CorrelationId}{Excepcion}",
                log.Metodo, log.Endpoint, log.Codigo, log.DuracionMs, log.Usuario, log.CorrelationId,
                log.Excepcion is null ? "" : " | Excepción=" + log.Excepcion);

            _queue.Enqueue(log);
        }
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
