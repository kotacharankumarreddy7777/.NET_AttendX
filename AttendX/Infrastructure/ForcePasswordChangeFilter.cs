using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AttendX.Infrastructure;

/// <summary>Users flagged MustChangePassword (e.g. the initial admin) are redirected to the change-password page until they change it.</summary>
public class ForcePasswordChangeFilter(UserManager<ApplicationUser> users) : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _users = users;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var controller = context.RouteData.Values["controller"]?.ToString();
        if (http.User.Identity?.IsAuthenticated == true && !string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase) && !string.Equals(controller, "Error", StringComparison.OrdinalIgnoreCase))
        {
            var user = await _users.GetUserAsync(http.User);
            if (user != null && user.MustChangePassword)
            {
                context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
                return;
            }
        }
        await next();
    }
}
