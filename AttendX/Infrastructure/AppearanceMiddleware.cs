namespace AttendX.Infrastructure;

/// <summary>Loads the current user's UI preferences into HttpContext.Items so layouts and views can apply them.</summary>
public class AppearanceMiddleware
{
    private readonly RequestDelegate _next;
    public AppearanceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAppearanceService appearance)
    {
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            var prefs = await appearance.GetPreferencesAsync(userId);
            context.Items["UserPreferences"] = prefs;
            context.Items["AppearanceReady"] = true;
        }
        await _next(context);
    }
}
