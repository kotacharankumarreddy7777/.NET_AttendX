namespace AttendX.Services;

public interface INoticeService
{
    Task<List<Notice>> VisibleAsync(int take = 50);
    Task<int> RecentCountAsync();
}

/// <summary>Notices visible to the logged-in user (Admin: all, Faculty/HOD: All+Faculty+own branch, Student: All+Student+own branch/section).</summary>
public class NoticeService(AppDbContext db, ICurrentUser user, IClock clock) : INoticeService
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly IClock _clock = clock;

    public async Task<List<Notice>> VisibleAsync(int take = 50)
    {
        var now = _clock.NowUtc;
        IQueryable<Notice> q = _db.Notices.AsNoTracking().Where(n => n.ExpiresAt == null || n.ExpiresAt >= now);
        if (_user.IsAdmin) { }
        else if (_user.IsHod || _user.IsFaculty)
        {
            var f = await _user.GetFacultyAsync();
            int? bid = f?.BranchId;
            q = q.Where(n => n.Audience == "All" || n.Audience == "Faculty" || (n.Audience == "Branch" && ((bid == null && n.BranchId == null) || (bid != null && n.BranchId == bid))) || (n.Audience == "Section" && (bid == null || n.BranchId == null || n.BranchId == bid)));
        }
        else
        {
            var uid = _user.UserId;
            var s = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.IdentityUserId == uid);
            if (s == null) return new List<Notice>();
            int branchId = s.BranchId, sectionId = s.SectionId;
            q = q.Where(n => n.Audience == "All" || n.Audience == "Student" || (n.Audience == "Branch" && (n.BranchId == branchId || n.BranchId == null))
                             || (n.Audience == "Section" && n.SectionId == sectionId && (n.BranchId == branchId || n.BranchId == null)));
        }
        return await q.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.PublishedAt).Take(take).ToListAsync();
    }

    public async Task<int> RecentCountAsync()
    {
        var since = _clock.NowUtc.AddDays(-7);
        return (await VisibleAsync(100)).Count(n => n.PublishedAt >= since);
    }
}
