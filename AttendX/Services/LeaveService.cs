namespace AttendX.Services;

public interface ILeaveService
{
    Task<List<LeaveType>> GetLeaveTypesAsync();
    Task<List<LeaveApplication>> GetLeaveApplicationsAsync(int? studentId, string? status);
    Task<LeaveApplication?> GetLeaveApplicationAsync(int id);
    Task<(bool Ok, string? Error)> ApplyLeaveAsync(int studentId, int leaveTypeId, DateTime startDate, DateTime endDate, string reason);
    Task<(bool Ok, string? Error)> ReviewLeaveAsync(int id, string status, string? adminResponse);
    Task<LeaveBalance?> GetLeaveBalanceAsync(int studentId);
}

public class LeaveService(AppDbContext db, ICurrentUser user, INotificationService notification) : ILeaveService
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly INotificationService _notification = notification;

    public async Task<List<LeaveType>> GetLeaveTypesAsync()
        => await _db.LeaveTypes.AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.Name).ToListAsync();

    public async Task<List<LeaveApplication>> GetLeaveApplicationsAsync(int? studentId, string? status)
    {
        var q = _db.LeaveApplications.AsNoTracking().Include(l => l.Student).Include(l => l.LeaveType).AsQueryable();
        if (studentId != null) q = q.Where(l => l.StudentId == studentId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(l => l.Status == status);
        return await q.OrderByDescending(l => l.AppliedAt).ToListAsync();
    }

    public async Task<LeaveApplication?> GetLeaveApplicationAsync(int id)
        => await _db.LeaveApplications.Include(l => l.Student).Include(l => l.LeaveType).FirstOrDefaultAsync(l => l.Id == id);

    public async Task<(bool Ok, string? Error)> ApplyLeaveAsync(int studentId, int leaveTypeId, DateTime startDate, DateTime endDate, string reason)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId && s.IsActive);
        if (student == null) return (false, "Student not found.");

        var leaveType = await _db.LeaveTypes.FirstOrDefaultAsync(l => l.Id == leaveTypeId && l.IsActive);
        if (leaveType == null) return (false, "Invalid leave type.");

        if (startDate > endDate) return (false, "Start date must be before end date.");
        if (startDate < DateTime.Today.AddDays(-30)) return (false, "Cannot apply for leave older than 30 days.");

        var totalDays = (int)(endDate - startDate).TotalDays + 1;
        var balance = await _db.LeaveBalances.FirstOrDefaultAsync(lb => lb.StudentId == studentId);
        if (balance != null && balance.RemainingDays < totalDays)
            return (false, $"Insufficient leave balance. Remaining: {balance.RemainingDays} days.");

        var app = new LeaveApplication
        {
            StudentId = studentId,
            LeaveTypeId = leaveTypeId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = reason,
            Status = "Pending"
        };
        _db.LeaveApplications.Add(app);
        await _db.SaveChangesAsync();

        // Notify HOD/Admin about new leave application
        await _notification.SendEmailAsync("admin@attendx.edu.in", "New Leave Application",
            $"{student.FullName} ({student.StudentID}) applied for {leaveType.Name} from {startDate:dd MMM yyyy} to {endDate:dd MMM yyyy}.");

        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> ReviewLeaveAsync(int id, string status, string? adminResponse)
    {
        var app = await _db.LeaveApplications.FirstOrDefaultAsync(l => l.Id == id);
        if (app == null) return (false, "Leave application not found.");

        app.Status = status;
        app.AdminResponse = adminResponse;
        app.ReviewedBy = _user.DisplayName;
        app.ReviewedAt = DateTime.UtcNow;

        if (status == "Approved")
        {
            var balance = await _db.LeaveBalances.FirstOrDefaultAsync(lb => lb.StudentId == app.StudentId);
            if (balance != null)
            {
                balance.UsedDays += app.TotalDays;
            }
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<LeaveBalance?> GetLeaveBalanceAsync(int studentId)
        => await _db.LeaveBalances.AsNoTracking().FirstOrDefaultAsync(lb => lb.StudentId == studentId);
}

