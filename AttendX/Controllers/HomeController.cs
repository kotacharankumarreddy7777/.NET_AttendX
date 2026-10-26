namespace AttendX.Controllers;

/// <summary>Landing page with the three role cards (Admin / Faculty / Student). Signed-in users are sent to their own dashboard.</summary>
[AllowAnonymous]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return User.IsInRole(RoleNames.Student) && !User.IsInRole(RoleNames.Admin) ? RedirectToAction("Index", "Student") : RedirectToAction("Index", "Dashboard");
        return View();
    }
}
