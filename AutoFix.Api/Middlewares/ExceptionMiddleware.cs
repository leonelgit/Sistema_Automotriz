using AutoFix.Api.Exceptions;
using AutoFix.Contracts;

namespace AutoFix.Api.Middlewares;

/// <summary>
/// (2) Middleware global de excepciones: atrapa cualquier error y responde con un mensaje claro.
/// Nunca devuelve el stack trace ni el mensaje interno al usuario (eso queda solo en el log).
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var (status, error, mensaje) = ExceptionMapper.Map(ex);
            var correlationId = context.CorrelationId();

            if (status >= 500)
                _logger.LogError(ex, "Error no controlado. CorrelationId={CorrelationId} {Metodo} {Ruta}",
                    correlationId, context.Request.Method, context.Request.Path);
            else
                _logger.LogWarning("Error controlado {Status}: {Mensaje}. CorrelationId={CorrelationId}",
                    status, ex.Message, correlationId);

            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.Headers[CorrelationIdMiddleware.Header] = correlationId;
            await context.Response.WriteAsJsonAsync(new ApiError
            {
                Status = status,
                Error = error,
                Mensaje = mensaje,
                CorrelationId = correlationId
            });
        }
    }
}
