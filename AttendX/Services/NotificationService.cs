namespace AttendX.Services;

public interface INotificationService
{
    Task NotifyAsync(string audience, string title, string body, int? branchId = null, int? sectionId = null);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IClock? _clock;

    public NotificationService(AppDbContext db, IClock? clock = null)
    {
        _db = db;
        _clock = clock;
    }

    public async Task NotifyAsync(string audience, string title, string body, int? branchId = null, int? sectionId = null)
    {
        if (string.IsNullOrWhiteSpace(audience)) throw new ArgumentException("Audience is required.", nameof(audience));
        var valid = new[] { "All", "Faculty", "Student", "Branch", "Section" };
        if (!valid.Contains(audience, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown audience '{audience}'. Use one of: {string.Join(", ", valid)}.", nameof(audience));
        var normalized = valid.First(v => v.Equals(audience, StringComparison.OrdinalIgnoreCase));
        _db.Notices.Add(new Notice
        {
            Title = title,
            Body = body,
            Audience = normalized,
            BranchId = branchId,
            SectionId = sectionId,
            PublishedAt = _clock?.Now ?? DateTime.UtcNow,
            CreatedBy = "System"
        });
        await _db.SaveChangesAsync();
    }
}
