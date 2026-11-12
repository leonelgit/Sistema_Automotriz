namespace AutoFix.Api.Middlewares;

/// <summary>
/// (1) Asigna un identificador único a cada petición para seguirla de punta a punta.
/// Si el cliente (por ejemplo el MVC) ya envía X-Correlation-ID, se reutiliza.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string Header = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            id = Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = id;
        context.TraceIdentifier = id;
        context.Response.Headers[Header] = id;

        await _next(context);
    }
}

public static class HttpContextCorrelationExtensions
{
    public static string CorrelationId(this HttpContext ctx) =>
        ctx.Items["CorrelationId"]?.ToString() ?? ctx.TraceIdentifier;
}
