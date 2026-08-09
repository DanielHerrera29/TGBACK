namespace TransportesGutierrez.Api.Services;

public sealed class PdfRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PdfRetentionService> _logger;

    public PdfRetentionService(IServiceScopeFactory scopeFactory, ILogger<PdfRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SupabaseService>();
                var deleted = await db.EliminarPdfsOrdenesExpiradosAsync(stoppingToken);
                if (deleted > 0) _logger.LogInformation("Se eliminaron {Cantidad} PDFs de ordenes con mas de 31 dias", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo ejecutar la retencion de PDFs de ordenes");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
