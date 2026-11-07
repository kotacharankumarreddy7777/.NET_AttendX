using Microsoft.Extensions.Options;

namespace AttendX.Services;

public class AiDiagnosticsBackgroundService(
    IServiceProvider services,
    IOptions<AiOptions> aiOpts,
    ILogger<AiDiagnosticsBackgroundService> logger) : BackgroundService
{
    private readonly IServiceProvider _services = services;
    private readonly AiOptions _opts = aiOpts.Value;
    private readonly ILogger<AiDiagnosticsBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI Diagnostics Background Service started. Interval: {Interval} minutes", _opts.DiagnosticsIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _opts.DiagnosticsIntervalMinutes)), stoppingToken);
                await RunDiagnosticsAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Diagnostics Background Service encountered an error");
            }
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.Now;
        var today = now.Date;

        // Gather live system health metrics
        int totalStudents = 0, activeSubjects = 0, totalAttendanceToday = 0;
        int anomalies = 0;

        try
        {
            totalStudents = await db.Students.CountAsync(s => s.IsActive);
            activeSubjects = await db.Subjects.CountAsync(s => s.IsActive);
            totalAttendanceToday = await db.Attendance.CountAsync(a => a.Date == today);
            var year = await db.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent); 
            int auditFailures = await db.AuditLogs.CountAsync(a => a.Action == "LoginFailed" && a.Timestamp >= today.AddHours(-1));
            string yearStatus = year != null ? $"Active ({year.Name})" : "MISSING - No current academic year!";
            if (year == null) anomalies++;

            int backupFailures = await db.BackupHistories.CountAsync(b => b.Status != "Success" && b.At >= today.AddDays(-7));
            if (backupFailures > 0) anomalies++;
            if (auditFailures > 5) anomalies++;

            string metrics = $"""
                Current Time: {now:yyyy-MM-dd HH:mm:ss}
                Active Students: {totalStudents}
                Active Subjects: {activeSubjects}
                Attendance Records Today: {totalAttendanceToday}
                Current Academic Year: {yearStatus}
                Login Failures (Last Hour): {auditFailures}
                Backup Failures (Last 7 Days): {backupFailures}
                Anomalies Detected: {anomalies}
                """;

            var (reportText, healthScore) = await aiService.EvaluateHealthAsync(metrics);

            var report = new AiDiagnosticReport
            {
                CreatedAt = now,
                ReportText = reportText,
                HealthScore = healthScore
            };

            db.AiDiagnosticReports.Add(report);
            await db.SaveChangesAsync();

            _logger.LogInformation("AI Diagnostics Report Generated. Score: {Score}, Anomalies: {Anomalies}", healthScore, anomalies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI Diagnostic report");
        }
    }
}
