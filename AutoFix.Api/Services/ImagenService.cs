using AutoFix.Api.Exceptions;

namespace AutoFix.Api.Services;

/// <summary>
/// Módulo 7. Guarda imágenes JPG/JPEG/PNG de máximo 5 MB en wwwroot/uploads/{carpeta}
/// con nombre único (Guid + extensión) y devuelve la RUTA relativa que se guarda en SQLite.
/// </summary>
public class ImagenService
{
    public const long TamanoMaximo = 5 * 1024 * 1024; // 5 MB
    private static readonly string[] Permitidas = { ".jpg", ".jpeg", ".png" };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImagenService> _logger;

    public ImagenService(IWebHostEnvironment env, ILogger<ImagenService> logger)
    {
        _env = env;
        _logger = logger;
    }

    private string WebRoot => string.IsNullOrEmpty(_env.WebRootPath)
        ? Path.Combine(_env.ContentRootPath, "wwwroot")
        : _env.WebRootPath;

    public async Task<string> GuardarAsync(IFormFile? archivo, string carpeta)
    {
        if (archivo is null || archivo.Length == 0)
            throw new BusinessException("Debe adjuntar una imagen.");

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (!Permitidas.Contains(ext))
            throw new BusinessException("Solo se permiten imágenes JPG, JPEG y PNG.");

        if (archivo.Length > TamanoMaximo)
            throw new BusinessException("La imagen no puede superar 5 MB.");

        // Validación extra: revisar la "firma" del archivo, no solo la extensión.
        if (!await EsImagenRealAsync(archivo, ext))
            throw new BusinessException("El archivo no es una imagen JPG/PNG válida.");

        var nombre = $"{Guid.NewGuid()}{ext}";
        var directorio = Path.Combine(WebRoot, "uploads", carpeta);
        Directory.CreateDirectory(directorio);

        var rutaFisica = Path.Combine(directorio, nombre);
        await using (var stream = File.Create(rutaFisica))
        {
            await archivo.CopyToAsync(stream);
        }

        var rutaRelativa = $"uploads/{carpeta}/{nombre}";
        _logger.LogInformation("Imagen guardada en {Ruta} ({Bytes} bytes)", rutaRelativa, archivo.Length);
        return rutaRelativa;
    }

    public void Eliminar(string? rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa)) return;
        try
        {
            var fisica = Path.GetFullPath(Path.Combine(WebRoot, rutaRelativa.Replace('/', Path.DirectorySeparatorChar)));
            var raiz = Path.GetFullPath(Path.Combine(WebRoot, "uploads"));
            if (fisica.StartsWith(raiz) && File.Exists(fisica)) File.Delete(fisica);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo eliminar la imagen {Ruta}", rutaRelativa);
        }
    }

    private static async Task<bool> EsImagenRealAsync(IFormFile archivo, string ext)
    {
        var cabecera = new byte[8];
        await using var s = archivo.OpenReadStream();
        var leidos = await s.ReadAsync(cabecera.AsMemory(0, 8));
        if (leidos < 4) return false;

        // JPEG: FF D8 FF   |   PNG: 89 50 4E 47 0D 0A 1A 0A
        var esJpeg = cabecera[0] == 0xFF && cabecera[1] == 0xD8 && cabecera[2] == 0xFF;
        var esPng = cabecera[0] == 0x89 && cabecera[1] == 0x50 && cabecera[2] == 0x4E && cabecera[3] == 0x47;
        return ext == ".png" ? esPng : esJpeg;
    }
}
