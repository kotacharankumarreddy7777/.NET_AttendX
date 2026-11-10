using Microsoft.Extensions.Logging;

namespace AttendX.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId = null, string? oldValues = null, string? newValues = null,
                  string? reason = null, int? branchId = null, string? details = null, string? actorName = null, string? actorId = null);
}

/// <summary>Writes an AuditLog row (who, what, old/new value, when). Never pass passwords or secrets into it.</summary>
public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock? _clock;
    private readonly ILogger<AuditService>? _log;

    public AuditService(AppDbContext db, ICurrentUser user, IClock? clock = null, ILogger<AuditService>? log = null)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _log = log;
    }

    public async Task LogAsync(string action, string entityType, string? entityId = null, string? oldValues = null, string? newValues = null,
                               string? reason = null, int? branchId = null, string? details = null, string? actorName = null, string? actorId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock?.Now ?? DateTime.Now,
            ActorId = actorId ?? _user.UserId,
            ActorName = actorName ?? _user.DisplayName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            Reason = reason,
            BranchId = branchId,
            Details = details,
            IpAddress = _user.IpAddress
        });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _log?.LogError(ex, "Audit log write failed for action {Action} on {EntityType} {EntityId}", action, entityType, entityId);
        }
    }
}
