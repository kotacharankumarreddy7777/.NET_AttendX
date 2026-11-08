using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AttendX.Infrastructure;

public class ForcePasswordChangeFilter(UserManager<ApplicationUser> users) : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _users = users;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (http.User?.Identity?.IsAuthenticated == true)
        {
            var ctrl = context.RouteData.Values["controller"]?.ToString() ?? "";
            var act = context.RouteData.Values["action"]?.ToString() ?? "";
            bool allowed = string.Equals(ctrl, "Account", StringComparison.OrdinalIgnoreCase) && (string.Equals(act, "ChangePassword", StringComparison.OrdinalIgnoreCase) || string.Equals(act, "Logout", StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                var user = await _users.GetUserAsync(http.User);
                if (user != null && user.MustChangePassword)
                {
                    context.Result = new Microsoft.AspNetCore.Mvc.RedirectToActionResult("ChangePassword", "Account", null);
                    return;
                }
            }
        }
        await next();
    }
}
