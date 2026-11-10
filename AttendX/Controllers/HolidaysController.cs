namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class HolidaysController(AppDbContext db, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index() => View(await _db.Holidays.AsNoTracking().OrderBy(h => h.Date).ToListAsync());

    public IActionResult Create() => View("Form", new Holiday { Date = DateTime.UtcNow.Date });

    public async Task<IActionResult> Edit(int id)
    {
        var h = await _db.Holidays.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return h == null ? NotFound() : View("Form", h);
    }

    [HttpPost]
    public async Task<IActionResult> Save([Bind("Id,Date,Name,Type")] Holiday m)
    {
        if (m.Name == null || string.IsNullOrWhiteSpace(m.Name)) ModelState.AddModelError(nameof(m.Name), "Name is required.");
        if (m.Type is not ("Holiday" or "Event" or "Exam")) ModelState.AddModelError(nameof(m.Type), "Choose Holiday, Event or Exam.");
        if (!ModelState.IsValid) return View("Form", m);
        m.Date = m.Date.Date;
        Holiday h;
        if (m.Id > 0)
        {
            var existing = await _db.Holidays.FindAsync(m.Id);
            if (existing == null) { TempData["Error"] = "That holiday no longer exists."; return RedirectToAction(nameof(Index)); }
            h = existing;
        }
        else { h = new Holiday(); _db.Holidays.Add(h); }
        h.Date = m.Date; h.Name = (m.Name ?? "").Trim(); h.Type = m.Type;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(m.Id > 0 ? "HolidayUpdated" : "HolidayCreated", "Holiday", h.Date.ToString("yyyy-MM-dd"), newValues: $"{h.Type}: {h.Name}");
        TempData["Success"] = "Saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var h = await _db.Holidays.FindAsync(id);
        if (h != null) { try { _db.Holidays.Remove(h); await _db.SaveChangesAsync(); await _audit.LogAsync("HolidayDeleted", "Holiday", h.Date.ToString("yyyy-MM-dd"), oldValues: h.Name); } catch (DbUpdateException) { TempData["Error"] = "Could not delete this holiday."; } }
        return RedirectToAction(nameof(Index));
    }
}
