namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class BackupController(IBackupService backup, IMasterDataExportService export) : Controller
{
    private readonly IBackupService _backup = backup;
    private readonly IMasterDataExportService _export = export;

    /// <summary>All admin / faculty / student / subject / timetable data in one Excel workbook.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportExcel()
        => File(await _export.BuildAsync(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"AttendX_Master_Data_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");

    public async Task<IActionResult> Index() => View(await _backup.GetAsync());

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var (ok, msg) = await _backup.CreateAsync();
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Restore(string fileName, string confirm)
    {
        if (!string.Equals(confirm?.Trim(), "RESTORE", StringComparison.Ordinal))
        { TempData["Error"] = "Type RESTORE in capital letters to confirm."; return RedirectToAction(nameof(Index)); }
        var (ok, msg) = await _backup.RestoreAsync(fileName);
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index));
    }
}
