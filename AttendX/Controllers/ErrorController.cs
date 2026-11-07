namespace AttendX.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("Error/{code?}")]
    public IActionResult Index(int? code)
    {
        var c = code ?? 500;
        var vm = new ErrorViewModel { Code = c };
        if (c == 404) { vm.Title = "Page not found"; vm.Message = "The page you are looking for does not exist or was moved."; }
        else if (c == 403) { vm.Title = "Access denied"; vm.Message = "You do not have permission to open this page."; }
        else if (c == 401) { vm.Title = "Please sign in"; vm.Message = "Your session expired. Sign in again."; }
        Response.StatusCode = c;
        return View(vm);
    }
}
