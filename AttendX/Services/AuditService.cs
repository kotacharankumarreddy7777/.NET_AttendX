namespace AttendX.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId = null, string? oldValues = null, string? newValues = null,
                  string? reason = null, int? branchId = null, string? details = null, string? actorName = null, string? actorId = null);
}

/// <summary>Writes an AuditLog row (who, what, old/new value, when). Never pass passwords or secrets into it.</summary>
public class AuditService(AppDbContext db, ICurrentUser user) : IAuditService
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;

    public async Task LogAsync(string action, string entityType, string? entityId = null, string? oldValues = null, string? newValues = null,
                               string? reason = null, int? branchId = null, string? details = null, string? actorName = null, string? actorId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.Now,
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
        await _db.SaveChangesAsync();
    }
}
