using System.Net.Http.Headers;

namespace AutoFix.Mvc.Services;

/// <summary>
/// DelegatingHandler: antes de cada petición a la API agrega
///   Authorization: Bearer {jwt}  (tomado de la cookie de sesión del usuario)
///   X-Correlation-ID: {id}       (mismo id para todas las llamadas de una misma acción del MVC)
/// </summary>
public class JwtHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;
    public JwtHandler(IHttpContextAccessor accessor) => _accessor = accessor;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var ctx = _accessor.HttpContext;
        if (ctx != null)
        {
            var token = ctx.User.FindFirst(UserExtensions.TokenClaim)?.Value;
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (!ctx.Items.TryGetValue("CorrelationId", out var cid) || cid is null)
            {
                cid = Guid.NewGuid().ToString();
                ctx.Items["CorrelationId"] = cid;
            }
            request.Headers.Remove("X-Correlation-ID");
            request.Headers.Add("X-Correlation-ID", cid.ToString());
        }
        return base.SendAsync(request, ct);
    }
}
