using System.Text;
using System.Threading.Channels;
using AutoFix.Api.Data;
using AutoFix.Api.Entities;

namespace AutoFix.Api.Logging;

/// <summary>Cola en memoria: la petición HTTP no espera a que el log se escriba.</summary>
public class LogQueue
{
    private readonly Channel<LogSolicitud> _channel = Channel.CreateUnbounded<LogSolicitud>();
    public void Enqueue(LogSolicitud log) => _channel.Writer.TryWrite(log);
    public ChannelReader<LogSolicitud> Reader => _channel.Reader;
}

/// <summary>
/// Servicio en segundo plano que guarda cada log en:
///  - un archivo de texto diario: logs/autofix-AAAAMMDD.log
///  - la tabla LogsSolicitudes de SQLite (para consultarlos con GET /api/logs)
/// </summary>
public class LogWriterService : BackgroundService
{
    private readonly LogQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LogWriterService> _logger;
    private readonly string _carpeta;

    public LogWriterService(LogQueue queue, IServiceScopeFactory scopes, ILogger<LogWriterService> logger, IWebHostEnvironment env)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
        _carpeta = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(_carpeta);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lote = new List<LogSolicitud>();
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                while (lote.Count < 100 && _queue.Reader.TryRead(out var item)) lote.Add(item);
                await GuardarAsync(lote);
                lote.Clear();
            }
        }
        catch (OperationCanceledException) { /* apagado normal */ }
    }

    private async Task GuardarAsync(List<LogSolicitud> lote)
    {
        // 1) Archivo
        try
        {
            var sb = new StringBuilder();
            foreach (var l in lote)
            {
                sb.Append($"{l.Fecha:yyyy-MM-dd HH:mm:ss} | {l.Codigo} | {l.Metodo} {l.Endpoint} | {l.DuracionMs} ms | " +
                          $"Usuario: {l.Usuario} | CorrelationId: {l.CorrelationId}");
                if (l.Excepcion != null) sb.Append($" | Excepción: {l.Excepcion}");
                sb.AppendLine();
                if (l.Detalle != null && l.Codigo >= 500) sb.AppendLine("    " + l.Detalle.Replace(Environment.NewLine, Environment.NewLine + "    "));
            }
            var archivo = Path.Combine(_carpeta, $"autofix-{DateTime.Now:yyyyMMdd}.log");
            await File.AppendAllTextAsync(archivo, sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo escribir el archivo de logs");
        }

        // 2) Base de datos
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoFixDbContext>();
            db.LogsSolicitudes.AddRange(lote);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron guardar los logs en la base de datos");
        }
    }
}
