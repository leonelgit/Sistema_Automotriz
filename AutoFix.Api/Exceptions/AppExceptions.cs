using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Exceptions;

/// <summary>Excepción base con código HTTP. Su mensaje SÍ se muestra al usuario.</summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { }
    public abstract int StatusCode { get; }
}

/// <summary>Regla de negocio violada (400).</summary>
public class BusinessException : AppException
{
    public BusinessException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status400BadRequest;
}

/// <summary>Recurso inexistente (404).</summary>
public class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status404NotFound;
}

/// <summary>Conflicto: duplicado o concurrencia (409).</summary>
public class ConflictException : AppException
{
    public ConflictException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status409Conflict;
}

/// <summary>El usuario está autenticado pero no puede hacer esto (403).</summary>
public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message) { }
    public override int StatusCode => StatusCodes.Status403Forbidden;
}

/// <summary>
/// Traduce cualquier excepción a (código HTTP, mensaje seguro para el usuario).
/// Lo usan el middleware de excepciones y el de logs para que ambos reporten el mismo código.
/// </summary>
public static class ExceptionMapper
{
    public static (int Status, string Error, string Mensaje) Map(Exception ex) => ex switch
    {
        AppException app => (app.StatusCode, Titulo(app.StatusCode), app.Message),

        DbUpdateConcurrencyException => (409, "Conflicto de concurrencia",
            "Otro usuario modificó este registro al mismo tiempo. La operación no se aplicó; recargue los datos e intente de nuevo."),

        DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } } => (409, "Conflicto de datos",
            "La operación viola una restricción de la base de datos (registro duplicado o valor no permitido)."),

        SqliteException { SqliteErrorCode: 5 or 6 } => (409, "Base de datos ocupada",
            "La base de datos está ocupada por otra operación. Intente de nuevo en unos segundos."),

        BadHttpRequestException bad => (bad.StatusCode, Titulo(bad.StatusCode), "La solicitud no es válida."),

        OperationCanceledException => (499, "Solicitud cancelada", "La solicitud fue cancelada."),

        _ => (500, "Error interno",
            "Ocurrió un error inesperado. Comparta el Correlation ID con el administrador para revisarlo.")
    };

    private static string Titulo(int status) => status switch
    {
        400 => "Solicitud inválida",
        401 => "No autenticado",
        403 => "Prohibido",
        404 => "No encontrado",
        409 => "Conflicto",
        413 => "Archivo demasiado grande",
        _ => "Error"
    };
}
