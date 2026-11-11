namespace AttendX.Services;

/// <summary>
/// Runs database migration + seed off the request path after the host has started.
/// Readiness stays unhealthy until this completes successfully. Failures are logged; the host keeps serving
/// /health/live but readiness remains unhealthy so the platform stops routing traffic.
/// </summary>
public sealed class DatabaseInitializationService(IServiceProvider services, ReadinessState state, ILogger<DatabaseInitializationService> logger)
    : BackgroundService
{
    private readonly IServiceProvider _services = services;
    private readonly ReadinessState _state = state;
    private readonly ILogger<DatabaseInitializationService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DbSeeder>>();
            var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromMinutes(25));
            _logger.LogInformation("Database initialization started.");
            await seeder.SeedAsync(cts.Token);
            _state.MarkReady();
            _logger.LogInformation("Database initialization completed.");
        }
        catch (Exception ex)
        {
            _state.MarkFailed(ex.Message);
            _logger.LogError(ex, "Database initialization failed. Readiness remains Unhealthy.");
        }
    }
}
