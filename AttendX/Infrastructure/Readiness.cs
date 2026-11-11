using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;

namespace AttendX.Infrastructure;

/// <summary>
/// Tracks whether startup initialization (migrations + seed) has completed. Readiness reports Healthy only when ready.
/// Never returns database error details to callers — just a status string.
/// </summary>
public sealed class ReadinessState
{
    private volatile int _ready;
    private volatile int _failed;
    private string? _failure;

    public bool IsReady => _ready == 1;
    public bool IsFailed => _failed == 1;
    public string? Failure => _failure;

    public void MarkReady() { _ready = 1; _failed = 0; _failure = null; }
    public void MarkFailed(string reason) { _failed = 1; _failure = reason; }
}

public sealed class DatabaseHealthCheck(AppDbContext db, ReadinessState state) : IHealthCheck
{
    private readonly AppDbContext _db = db;
    private readonly ReadinessState _state = state;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_state.IsReady)
            return HealthCheckResult.Unhealthy("Startup initialization in progress or failed.");

        try
        {
            // A cheap round-trip: open the connection and run a trivial statement.
            await _db.Database.CanConnectAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is not reachable.", ex);
        }
    }
}
