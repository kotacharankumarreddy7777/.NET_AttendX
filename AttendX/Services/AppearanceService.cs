namespace AttendX.Services;

/// <summary>Persistent per-user UI preferences plus admin-controlled institutional branding.</summary>
public interface IAppearanceService
{
    UserPreference GetPreferences(string userId);
    Task<UserPreference> GetPreferencesAsync(string userId, CancellationToken ct = default);
    Task SetPreferencesAsync(string userId, UserPreference prefs, CancellationToken ct = default);
    Task<InstitutionBranding> GetBrandingAsync(CancellationToken ct = default);
    Task SetBrandingAsync(InstitutionBranding branding, CancellationToken ct = default);
    /// <summary>CSS custom property assignments derived from branding + user preferences.</summary>
    string DesignTokens(InstitutionBranding branding, UserPreference prefs);
}

public class AppearanceService(AppDbContext db) : IAppearanceService
{
    private static readonly UserPreference Defaults = new();

    public UserPreference GetPreferences(string userId) => GetPreferencesAsync(userId).GetAwaiter().GetResult();

    public async Task<UserPreference> GetPreferencesAsync(string userId, CancellationToken ct = default)
    {
        var p = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        return p ?? new UserPreference { UserId = userId };
    }

    public async Task SetPreferencesAsync(string userId, UserPreference prefs, CancellationToken ct = default)
    {
        prefs.UserId = userId;
        prefs.UpdatedAt = DateTime.UtcNow;
        var existing = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing == null)
        {
            db.UserPreferences.Add(prefs);
        }
        else
        {
            existing.Theme = prefs.Theme;
            existing.AccentColor = prefs.AccentColor;
            existing.PrimaryColor = prefs.PrimaryColor;
            existing.SecondaryColor = prefs.SecondaryColor;
            existing.Density = prefs.Density;
            existing.FontSize = prefs.FontSize;
            existing.FontFamily = prefs.FontFamily;
            existing.BorderRadius = prefs.BorderRadius;
            existing.AnimationsEnabled = prefs.AnimationsEnabled;
            existing.SidebarCollapsed = prefs.SidebarCollapsed;
            existing.ContentWidth = prefs.ContentWidth;
            existing.UpdatedAt = prefs.UpdatedAt;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<InstitutionBranding> GetBrandingAsync(CancellationToken ct = default)
    {
        var b = await db.InstitutionBranding.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        return b ?? new InstitutionBranding { Id = 1 };
    }

    public async Task SetBrandingAsync(InstitutionBranding branding, CancellationToken ct = default)
    {
        branding.Id = 1;
        branding.UpdatedAt = DateTime.UtcNow;
        var existing = await db.InstitutionBranding.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (existing == null) db.InstitutionBranding.Add(branding);
        else
        {
            existing.InstitutionName = branding.InstitutionName;
            existing.ShortName = branding.ShortName;
            existing.PrimaryColor = branding.PrimaryColor;
            existing.SecondaryColor = branding.SecondaryColor;
            existing.LogoUrl = branding.LogoUrl;
            existing.UpdatedAt = branding.UpdatedAt;
        }
        await db.SaveChangesAsync(ct);
    }

    public string DesignTokens(InstitutionBranding branding, UserPreference prefs)
    {
        var accent = IsHex(prefs.AccentColor) ? prefs.AccentColor : Defaults.AccentColor;
        var primary = IsHex(prefs.PrimaryColor) ? prefs.PrimaryColor : Defaults.PrimaryColor;
        var secondary = IsHex(prefs.SecondaryColor) ? prefs.SecondaryColor : Defaults.SecondaryColor;
        var radius = prefs.BorderRadius switch
        {
            "none" => "0", "sm" => "0.25rem", "md" => "0.5rem", "lg" => "0.75rem", "full" => "9999px", _ => "0.5rem"
        };
        var density = prefs.Density switch
        {
            "compact" => "0.35rem 0.5rem", "comfortable" => "0.5rem 0.75rem", _ => "0.5rem 0.75rem"
        };
        var fontSize = prefs.FontSize switch
        {
            "small" => "0.85rem", "large" => "1.1rem", _ => "1rem"
        };
        var animations = prefs.AnimationsEnabled ? "" : ".btn,.card,.sidebar,.table-row{transition:none !important;animation:none !important;}";
        return $@"--ax-primary:{primary};--ax-secondary:{secondary};--ax-accent:{accent};--ax-radius:{radius};--ax-density-pad:{density};--ax-font-size:{fontSize};--ax-sidebar-collapsed:{prefs.SidebarCollapsed.ToString().ToLower()};--ax-animations:{prefs.AnimationsEnabled.ToString().ToLower()};{animations}";
    }

    private static bool IsHex(string? c) => !string.IsNullOrWhiteSpace(c) && System.Text.RegularExpressions.Regex.IsMatch(c!, @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$");
}
