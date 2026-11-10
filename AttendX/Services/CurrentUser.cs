using System.Security.Claims;

namespace AttendX.Services;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string DisplayName { get; }
    bool IsAdmin { get; }
    bool IsHod { get; }
    bool IsFaculty { get; }
    string? IpAddress { get; }
    Task<Faculty?> GetFacultyAsync();
    Task<int?> GetScopeBranchIdAsync();
}

public class CurrentUser(IHttpContextAccessor http, AppDbContext db) : ICurrentUser
{
    private readonly IHttpContextAccessor _http = http;
    private readonly AppDbContext _db = db;
    private Faculty? _faculty;
    private bool _loaded;

    private ClaimsPrincipal? P => _http.HttpContext?.User;
    public bool IsAuthenticated => P?.Identity?.IsAuthenticated == true;
    public string? UserId => P?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string DisplayName
    {
        get
        {
            var id = UserId;
            if (id != null && _faculty != null && !string.IsNullOrWhiteSpace(_faculty.FullName)) return _faculty.FullName;
            var claim = P?.FindFirstValue(ClaimTypes.Name) ?? P?.FindFirstValue("FullName");
            if (!string.IsNullOrWhiteSpace(claim)) return claim;
            return P?.Identity?.Name ?? "system";
        }
    }
    public bool IsAdmin => P?.IsInRole(RoleNames.Admin) == true;
    public bool IsHod => P?.IsInRole(RoleNames.Hod) == true;
    public bool IsFaculty => P?.IsInRole(RoleNames.Faculty) == true;
    public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public async Task<Faculty?> GetFacultyAsync()
    {
        if (_loaded) return _faculty;
        _loaded = true;
        var id = UserId;
        if (id != null)
            _faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(f => f.IdentityUserId == id && f.IsActive);
        return _faculty;
    }

    public async Task<int?> GetScopeBranchIdAsync()
    {
        if (IsAdmin) return null;
        var f = await GetFacultyAsync();
        return f?.BranchId ?? -1;
    }

    public async Task<int?> ScopeBranchOrNullAsync()
    {
        var raw = await GetScopeBranchIdAsync();
        return raw is null or -1 ? null : raw;
    }
}
