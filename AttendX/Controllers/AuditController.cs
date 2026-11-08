namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOrHod)]
public class AuditController(AppDbContext db, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;

    public async Task<IActionResult> Index(string? action, string? actor, string? search, DateTime? from, DateTime? to, int page = 1)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (scope != null) q = q.Where(a => a.BranchId == scope);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var term = actor.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            q = q.Where(a => a.ActorName.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            q = q.Where(a =>
                (a.EntityId != null && a.EntityId.Contains(term)) ||
                (a.Details != null && a.Details.Contains(term)) ||
                (a.Reason != null && a.Reason.Contains(term)));
        }
        if (from != null) { var f = from.Value.Date; q = q.Where(a => a.Timestamp >= f); }
        if (to != null) { var t = to.Value.Date.AddDays(1); q = q.Where(a => a.Timestamp < t); }
        page = Math.Max(1, page);
        var list = new PagedList<AuditLog> { Page = page, PageSize = 40, TotalCount = await q.CountAsync() };
        list.Items = await q.OrderByDescending(a => a.Timestamp).Skip((page - 1) * 40).Take(40).ToListAsync();
        var aq = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (scope != null) aq = aq.Where(a => a.BranchId == scope);
        ViewBag.Actions = await aq.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        ViewBag.Action = action; ViewBag.Actor = actor; ViewBag.Search = search; ViewBag.From = from; ViewBag.To = to;
        return View(list);
    }
}
