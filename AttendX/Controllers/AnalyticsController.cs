namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AnyStaff)]
public class AnalyticsController(IAnalyticsService analytics) : Controller
{
    private readonly IAnalyticsService _analytics = analytics;

    public async Task<IActionResult> Index() => View(await _analytics.BuildAsync());
}
