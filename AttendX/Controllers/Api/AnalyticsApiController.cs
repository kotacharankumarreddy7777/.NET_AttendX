using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendX.Controllers.Api;

/// <summary>REST API for analytics: at-risk students, subject risks, section anomalies, weekly trend.</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/analytics")]
[Authorize(Policy = PolicyNames.AnyStaff)]
public class AnalyticsApiController(IAnalyticsService analytics) : ControllerBase
{
    private readonly IAnalyticsService _analytics = analytics;

    /// <summary>Full analytics snapshot: at-risk students, subject risks, section anomalies, weekly trend.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var vm = await _analytics.BuildAsync();
        return Ok(new
        {
            atRiskCount = vm.AtRiskCount,
            decliningCount = vm.DecliningCount,
            weeklyTrend = new { labels = vm.Weekly.Labels, values = vm.Weekly.Values },
            sections = vm.Sections.Select(s => new { s.Section, s.Recent, s.Previous, s.Average, s.ZScore }),
            subjects = vm.Subjects.Select(s => new { s.Subject, s.Percent, s.Threshold, s.Records }),
            atRiskStudents = vm.Students.Select(s => new { s.Code, s.Name, s.Section, s.Percent, s.Threshold, s.Risk, s.SlopePerWeek })
        });
    }
}
