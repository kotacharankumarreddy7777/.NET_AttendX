namespace AttendX.Services;

public interface INotificationService
{
    Task NotifyAsync(string audience, string title, string body, int? branchId = null, int? sectionId = null);
}

public class NotificationService(AppDbContext db) : INotificationService
{
    private readonly AppDbContext _db = db;

    public async Task NotifyAsync(string audience, string title, string body, int? branchId = null, int? sectionId = null)
    {
        _db.Notices.Add(new Notice
        {
            Title = title,
            Body = body,
            Audience = audience,
            BranchId = branchId,
            SectionId = sectionId,
            PublishedAt = DateTime.Now,
            CreatedBy = "System"
        });
        await _db.SaveChangesAsync();
    }
}
