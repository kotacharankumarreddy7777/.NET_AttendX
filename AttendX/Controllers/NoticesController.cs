using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize]
public class NoticesController(AppDbContext db, INoticeService notices, ICurrentUser user, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly INoticeService _notices = notices;
    private readonly ICurrentUser _user = user;
    private readonly IAuditService _audit = audit;

    /// <summary>Notice board for every role (what the signed-in user is allowed to see).</summary>
    public async Task<IActionResult> Board() => View(await _notices.VisibleAsync(100));

    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Index()
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.Notices.AsNoTracking().AsQueryable();
        if (scope.HasValue) q = q.Where(n => n.BranchId == scope);
        return View(await q.OrderByDescending(n => n.PublishedAt).Take(200).ToListAsync());
    }

    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Create() => View("Form", await Fill(new NoticeFormViewModel()));

    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Edit(int id)
    {
        var n = await _db.Notices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound();
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope.HasValue && n.BranchId != scope) return Forbid();
        return View("Form", await Fill(new NoticeFormViewModel { Id = n.Id, Title = n.Title, Body = n.Body, Audience = n.Audience, BranchId = n.BranchId, SectionId = n.SectionId, ExpiresAt = n.ExpiresAt, IsPinned = n.IsPinned }));
    }

    [Authorize(Policy = PolicyNames.AdminOrHod), HttpPost]
    public async Task<IActionResult> Save(NoticeFormViewModel m)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (m.Audience is not ("All" or "Faculty" or "Student" or "Branch" or "Section")) ModelState.AddModelError(nameof(m.Audience), "Invalid audience.");
        if (scope != null)
        {
            if (m.Audience is not ("Branch" or "Section")) ModelState.AddModelError(nameof(m.Audience), "HODs can post notices to their branch or a section only.");
            m.BranchId = scope;
        }
        if ((m.Audience is "Branch" or "Section") && m.BranchId == null) ModelState.AddModelError(nameof(m.BranchId), "Select a branch.");
        if (m.Audience == "Section" && m.SectionId == null) ModelState.AddModelError(nameof(m.SectionId), "Select a section.");
        if (m.Audience == "Section" && m.SectionId != null && !await _db.Sections.AnyAsync(s => s.Id == m.SectionId && s.BranchId == m.BranchId)) ModelState.AddModelError(nameof(m.SectionId), "Section does not belong to the branch.");
        if (!ModelState.IsValid) return View("Form", await Fill(m));

        Notice n;
        if (m.Id.HasValue)
        {
            var existing = await _db.Notices.FindAsync(m.Id.Value);
            if (existing == null) { TempData["Error"] = "That notice no longer exists."; return RedirectToAction(nameof(Index)); }
            if (scope.HasValue && existing.BranchId != scope) return Forbid();
            n = existing;
        }
        else { n = new Notice { PublishedAt = DateTime.UtcNow, CreatedBy = _user.DisplayName }; _db.Notices.Add(n); }
        n.Title = m.Title.Trim(); n.Body = m.Body.Trim(); n.Audience = m.Audience;
        n.BranchId = (m.Audience is "Branch" or "Section") ? m.BranchId : null;
        n.SectionId = m.Audience == "Section" ? m.SectionId : null;
        if (m.ExpiresAt != null) { var e = m.ExpiresAt.Value.Date.AddDays(1).AddSeconds(-1); n.ExpiresAt = DateTime.SpecifyKind(e, DateTimeKind.Utc); } else { n.ExpiresAt = null; } n.IsPinned = m.IsPinned;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "Could not save the notice.";
            return View("Form", await Fill(m));
        }
        await _audit.LogAsync(m.Id.HasValue ? "NoticeUpdated" : "NoticeCreated", "Notice", n.Id.ToString(), newValues: $"{n.Audience}: {n.Title}", branchId: n.BranchId);
        TempData["Success"] = "Notice saved.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.AdminOrHod), HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var n = await _db.Notices.FindAsync(id);
        var scope = await _user.GetScopeBranchIdAsync();
        if (n != null && (!scope.HasValue || n.BranchId == scope)) { _db.Notices.Remove(n); await _db.SaveChangesAsync(); await _audit.LogAsync("NoticeDeleted", "Notice", id.ToString(), oldValues: n.Title, branchId: n.BranchId); }
        return RedirectToAction(nameof(Index));
    }

    private async Task<NoticeFormViewModel> Fill(NoticeFormViewModel m)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        m.Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive && (!scope.HasValue || b.Id == scope)).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        var bid = m.BranchId ?? scope;
        if (bid != null) m.Sections = await _db.Sections.AsNoTracking().Where(s => s.BranchId == bid && s.IsActive).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        return m;
    }
}
