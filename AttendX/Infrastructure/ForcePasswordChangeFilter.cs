using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AttendX.Infrastructure;

/// <summary>Placeholder: MustChangePassword redirect disabled for plain text college project.</summary>
public class ForcePasswordChangeFilter(UserManager<ApplicationUser> users) : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _users = users;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        await next();
    }
}
