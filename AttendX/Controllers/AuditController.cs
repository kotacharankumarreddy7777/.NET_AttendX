namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOrHod)]
public class AuditController(AppDbContext db, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;

    private static string EscapeLike(string term)
        => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<IActionResult> Index(string? action, string? actor, string? search, DateTime? from, DateTime? to, int page = 1)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (scope.HasValue) q = q.Where(a => a.BranchId == scope);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var esc = EscapeLike(actor.Trim());
            q = q.Where(a => EF.Functions.Like(a.ActorName, "%" + esc + "%", "\\"));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var esc = EscapeLike(search.Trim());
            q = q.Where(a =>
                (a.EntityId != null && EF.Functions.Like(a.EntityId, "%" + esc + "%", "\\")) ||
                (a.Details != null && EF.Functions.Like(a.Details, "%" + esc + "%", "\\")) ||
                (a.Reason != null && EF.Functions.Like(a.Reason, "%" + esc + "%", "\\")));
        }
        if (from != null) { var f = from.Value.Date; q = q.Where(a => a.Timestamp >= f); }
        if (to != null) { var t = to.Value.Date.AddDays(1); q = q.Where(a => a.Timestamp < t); }
        page = Math.Max(1, page);
        var list = new PagedList<AuditLog> { Page = page, PageSize = 40, TotalCount = await q.CountAsync() };
        list.Items = await q.OrderByDescending(a => a.Timestamp).Skip((page - 1) * 40).Take(40).ToListAsync();
        // Action list for the filter dropdown — cache briefly, not an unbounded DISTINCT on every page load.
        var since = DateTime.UtcNow.AddDays(-90);
        var aq = _db.AuditLogs.AsNoTracking().Where(a => a.Timestamp >= since);
        if (scope.HasValue) aq = aq.Where(a => a.BranchId == scope);
        ViewBag.Actions = await aq.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        ViewBag.Action = action; ViewBag.Actor = actor; ViewBag.Search = search; ViewBag.From = from; ViewBag.To = to;
        return View(list);
    }
}
