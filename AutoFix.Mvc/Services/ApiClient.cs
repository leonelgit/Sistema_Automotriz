using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFix.Contracts;

namespace AutoFix.Mvc.Services;

/// <summary>Resultado de una llamada a la API.</summary>
public class ApiResult<T>
{
    public bool Ok { get; set; }
    public int Status { get; set; }
    public T? Data { get; set; }
    public string Mensaje { get; set; } = "";
    public List<string> Errores { get; set; } = new();
    public string? CorrelationId { get; set; }

    /// <summary>Mensaje listo para mostrar: incluye el código HTTP, detalles y el Correlation ID.</summary>
    public string MensajeCompleto
    {
        get
        {
            var texto = $"({Status}) {Mensaje}";
            if (Errores.Count > 0) texto += " " + string.Join(" ", Errores);
            if (!string.IsNullOrEmpty(CorrelationId)) texto += $" [Correlation ID: {CorrelationId}]";
            return texto;
        }
    }
}

/// <summary>La API respondió 401: el token no existe o expiró.</summary>
public class ApiUnauthorizedException : Exception
{
    public ApiUnauthorizedException(string mensaje) : base(mensaje) { }
}

/// <summary>
/// Envoltorio sobre HttpClient para consumir AutoFix.Api.
/// Es la ÚNICA forma en que el MVC obtiene o modifica datos.
/// </summary>
public class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public ApiClient(IHttpClientFactory factory) => _http = factory.CreateClient("AutoFixApi");

    public Task<ApiResult<T>> GetAsync<T>(string url) =>
        EnviarAsync<T>(new HttpRequestMessage(HttpMethod.Get, url));

    public Task<ApiResult<T>> PostAsync<T>(string url, object? body = null) =>
        EnviarAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }, options: Json) });

    public Task<ApiResult<T>> PutAsync<T>(string url, object body) =>
        EnviarAsync<T>(new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: Json) });

    public Task<ApiResult<object>> DeleteAsync(string url) =>
        EnviarAsync<object>(new HttpRequestMessage(HttpMethod.Delete, url));

    /// <summary>Envía un archivo como multipart/form-data (campo "archivo") más campos de texto opcionales.</summary>
    public async Task<ApiResult<T>> PostArchivoAsync<T>(string url, IFormFile? archivo, Dictionary<string, string?>? campos = null)
    {
        var content = new MultipartFormDataContent();
        if (archivo != null)
        {
            var stream = new StreamContent(archivo.OpenReadStream());
            stream.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrEmpty(archivo.ContentType) ? "application/octet-stream" : archivo.ContentType);
            content.Add(stream, "archivo", archivo.FileName);
        }
        if (campos != null)
            foreach (var (k, v) in campos)
                if (v != null) content.Add(new StringContent(v), k);

        return await EnviarAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = content });
    }

    private async Task<ApiResult<T>> EnviarAsync<T>(HttpRequestMessage req)
    {
        using (req)
        using (var resp = await _http.SendAsync(req))
        {
            var resultado = new ApiResult<T>
            {
                Status = (int)resp.StatusCode,
                Ok = resp.IsSuccessStatusCode,
                CorrelationId = resp.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids.FirstOrDefault() : null
            };

            if (resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode != HttpStatusCode.NoContent && resp.Content.Headers.ContentLength != 0)
                {
                    var texto = await resp.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(texto))
                        resultado.Data = JsonSerializer.Deserialize<T>(texto, Json);
                }
                return resultado;
            }

            var error = await LeerErrorAsync(resp);
            resultado.Mensaje = error.Mensaje;
            resultado.Errores = error.Errores;
            resultado.CorrelationId = error.CorrelationId ?? resultado.CorrelationId;

            // 401 fuera del login => la sesión expiró: el filtro ApiExceptionFilter redirige al login.
            var esAuth = req.RequestUri?.ToString().Contains("api/auth/", StringComparison.OrdinalIgnoreCase) == true;
            if (resp.StatusCode == HttpStatusCode.Unauthorized && !esAuth)
                throw new ApiUnauthorizedException(error.Mensaje);

            return resultado;
        }
    }

    private static async Task<ApiError> LeerErrorAsync(HttpResponseMessage resp)
    {
        var texto = await resp.Content.ReadAsStringAsync();
        try
        {
            var error = JsonSerializer.Deserialize<ApiError>(texto, Json);
            if (error != null && !string.IsNullOrEmpty(error.Mensaje)) return error;

            // ProblemDetails estándar (por si acaso)
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("title", out var title))
                return new ApiError { Mensaje = title.GetString() ?? "" };
        }
        catch { /* respuesta sin JSON */ }

        return new ApiError
        {
            Mensaje = resp.StatusCode switch
            {
                HttpStatusCode.Forbidden => "No tiene permisos para realizar esta acción.",
                HttpStatusCode.NotFound => "El recurso solicitado no existe.",
                HttpStatusCode.RequestEntityTooLarge => "El archivo es demasiado grande.",
                _ => string.IsNullOrWhiteSpace(texto) ? $"Error {(int)resp.StatusCode} de la API." : texto
            }
        };
    }
}
