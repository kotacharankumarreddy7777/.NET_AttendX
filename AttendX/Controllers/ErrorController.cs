using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AttendX.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    private readonly ILogger<ErrorController> _log;
    public ErrorController(ILogger<ErrorController> log) => _log = log;

    [Route("Error/{code?}")]
    public IActionResult Index(int? code)
    {
        var c = code ?? 500;
        var vm = new ErrorViewModel { Code = c };
        if (c == 404) { vm.Title = "Page not found"; vm.Message = "The page you are looking for does not exist or was moved."; }
        else if (c == 403) { vm.Title = "Access denied"; vm.Message = "You do not have permission to open this page."; }
        else if (c == 401) { vm.Title = "Please sign in"; vm.Message = "Your session expired. Sign in again."; }
        var exFeature = HttpContext.Features.Get<IExceptionHandlerFeature>();
        if (exFeature?.Error != null)
        {
            var ex = exFeature.Error;
            vm.RequestId = HttpContext.TraceIdentifier;
            _log.LogCritical(ex, "Unhandled exception on {Path} (RequestId {RequestId}, Code {Code})", exFeature.Path, vm.RequestId, c);
        }
        Response.StatusCode = c;
        return View(vm);
    }
}
