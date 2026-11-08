namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AnyStaff)]
public class DashboardController(IDashboardService dash) : Controller
{
    private readonly IDashboardService _dash = dash;

    /// <summary>One entry point - Admin and HOD share the admin dashboard; role priority is Admin > HOD > Faculty.</summary>
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Hod))
            return View("Admin", await _dash.BuildAdminOrHodAsync());
        var vm = await _dash.BuildFacultyAsync();
        if (vm == null) return View("NoFaculty");
        return View("Faculty", vm);
    }
}
