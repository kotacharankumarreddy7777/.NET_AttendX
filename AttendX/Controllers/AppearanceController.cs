namespace AttendX.Controllers;

using Microsoft.AspNetCore.Identity;

[Authorize]
public class AppearanceController(AppDbContext db, IAppearanceService appearance, UserManager<ApplicationUser> users, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IAppearanceService _appearance = appearance;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        var prefs = await _appearance.GetPreferencesAsync(user.Id);
        var branding = await _appearance.GetBrandingAsync();
        var isAdmin = User.IsInRole("Admin");
        ViewData["Tokens"] = _appearance.DesignTokens(branding, prefs);
        ViewData["Branding"] = branding;
        ViewData["IsAdmin"] = isAdmin;
        return View(prefs);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserPreference prefs)
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!IsValidHex(prefs.AccentColor) || !IsValidHex(prefs.PrimaryColor) || !IsValidHex(prefs.SecondaryColor))
        { TempData["Error"] = "Enter valid hex colors (e.g. #2563eb)."; return RedirectToAction(nameof(Index)); }
        prefs.UserId = user.Id;
        await _appearance.SetPreferencesAsync(user.Id, prefs);
        await _audit.LogAsync("PreferencesSaved", "UserPreference", user.Id, details: $"theme={prefs.Theme}, accent={prefs.AccentColor}, density={prefs.Density}");
        TempData["Success"] = "Appearance preferences saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = PolicyNames.AdminOnly)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBranding(InstitutionBranding b)
    {
        if (!IsValidHex(b.PrimaryColor) || !IsValidHex(b.SecondaryColor))
        { TempData["Error"] = "Enter valid hex colors."; return RedirectToAction(nameof(Index)); }
        await _appearance.SetBrandingAsync(b);
        await _audit.LogAsync("BrandingChanged", "InstitutionBranding", "1", details: $"name={b.InstitutionName}, primary={b.PrimaryColor}");
        TempData["Success"] = "Institution branding updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return Challenge();
        var defaults = new AttendX.Models.UserPreference { UserId = user.Id };
        await _appearance.SetPreferencesAsync(user.Id, defaults);
        await _audit.LogAsync("PreferencesReset", "UserPreference", user.Id);
        TempData["Success"] = "Appearance reset to defaults.";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsValidHex(string? c) => !string.IsNullOrWhiteSpace(c) &&
        System.Text.RegularExpressions.Regex.IsMatch(c!, @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$");
}
