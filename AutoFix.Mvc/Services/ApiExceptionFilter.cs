using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace AutoFix.Mvc.Services;

/// <summary>
/// - 401 de la API: cierra la sesión del MVC y manda al login.
/// - API apagada / inaccesible: muestra una página clara en lugar de un error técnico.
/// </summary>
public class ApiExceptionFilter : IAsyncExceptionFilter
{
    private readonly ITempDataDictionaryFactory _tempData;
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ITempDataDictionaryFactory tempData, ILogger<ApiExceptionFilter> logger)
    {
        _tempData = tempData;
        _logger = logger;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        var http = context.HttpContext;
        switch (context.Exception)
        {
            case ApiUnauthorizedException ex:
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                _tempData.GetTempData(http)["Error"] = "Su sesión expiró o no es válida. Inicie sesión de nuevo. " + ex.Message;
                context.Result = new RedirectToActionResult("Login", "Account",
                    new { returnUrl = http.Request.Path + http.Request.QueryString });
                context.ExceptionHandled = true;
                break;

            case HttpRequestException or TaskCanceledException:
                _logger.LogError(context.Exception, "No se pudo conectar con la API");
                context.Result = new ViewResult
                {
                    ViewName = "ApiNoDisponible",
                    ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState)
                    {
                        ["Detalle"] = context.Exception.Message
                    }
                };
                context.ExceptionHandled = true;
                break;
        }
    }
}
