using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AttendX.Hubs;

/// <summary>
/// Pushes live attendance updates to faculty dashboards and admin "Today's Classes" views.
/// Clients join groups by branch/section and by TimetableId. Server notifies that group when marks are saved.
/// </summary>
[Authorize]
public class AttendanceHub : Hub
{
    /// <summary>Join the group for one class so the client receives saved-count updates.</summary>
    public async Task JoinClassAsync(int timetableId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"class-{timetableId}");

    public async Task LeaveClassAsync(int timetableId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"class-{timetableId}");

    /// <summary>Join the admin "today" group (admin/HOD receive every saved attendance batch).</summary>
    public async Task JoinAdminTodayAsync()
    {
        if (Context.User is { } user && (user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.Hod)))
            await Groups.AddToGroupAsync(Context.ConnectionId, "admin-today");
    }

    public async Task LeaveAdminTodayAsync()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admin-today");
}

/// <summary>
/// Pushes notification toasts to signed-in users (admin/HOD receive new attendance queries; students receive their query updates).
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    /// <summary>Join the per-user group so new notifications arrive live.</summary>
    public async Task JoinMyNotificationsAsync()
    {
        if (Context.UserIdentifier is { } uid)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{uid}");
    }

    public async Task LeaveMyNotificationsAsync()
    {
        if (Context.UserIdentifier is { } uid)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{uid}");
    }
}
