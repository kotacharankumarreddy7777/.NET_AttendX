using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttendX.Controllers;

[Authorize]
public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AppDbContext db, IAuditService audit, ILogger<AccountController> log, IOptions<AttendXOptions> opt) : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn = signIn;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<AccountController> _log = log;
    private readonly AttendXOptions _opt = opt.Value;

    [AllowAnonymous, HttpGet("admin/login")] public IActionResult AdminLogin(string? returnUrl = null) => RoleScreen("admin", returnUrl);
    [AllowAnonymous, HttpGet("faculty/login")] public IActionResult FacultyLogin(string? returnUrl = null) => RoleScreen("faculty", returnUrl);
    [AllowAnonymous, HttpGet("student/login")] public IActionResult StudentLogin(string? returnUrl = null) => RoleScreen("student", returnUrl);

    /// <summary>Cookie middleware sends unauthenticated users here; show the landing page where they pick a role.</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => RedirectToAction("Index", "Home");

    private IActionResult RoleScreen(string role, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        if (!string.IsNullOrEmpty(returnUrl) && !Url.IsLocalUrl(returnUrl)) returnUrl = null;
        return View("RoleLogin", new RoleLoginViewModel { Role = role, ReturnUrl = returnUrl });
    }

    /// <summary>
    /// Faculty/Student: Institution ID + email + password must ALL belong to the same account. Admin: email + password.
    /// Any mismatch gives the same generic message (no user enumeration) and counts as a failed attempt (lockout).
    /// Passwords are verified via Identity (CheckPasswordAsync); accounts are created only by seeder/admin, never at login.
    /// </summary>
    [AllowAnonymous, HttpPost("account/signin")]
    public async Task<IActionResult> Signin(RoleLoginViewModel m)
    {
        const string generic = "Institution ID, email or password is incorrect.";
        try
        {
            var role = (m.Role ?? "").ToLowerInvariant();
            if (role is not ("admin" or "faculty" or "student")) return RedirectToAction("Index", "Home");
            m.Role = role;
            m.InstitutionId = (m.InstitutionId ?? "").Trim().ToUpperInvariant();
            m.Email = (m.Email ?? "").Trim();
            m.Password = (m.Password ?? "").Trim();

            bool needsId = role != "admin";
            if (needsId && string.IsNullOrWhiteSpace(m.InstitutionId))
            {
                return Fail(m, "Institution ID is required.");
            }
            if (!ModelState.IsValid)
            {
                var err = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
                return Fail(m, err);
            }

            ApplicationUser? user = null;

            if (role == "admin")
            {
                if (string.IsNullOrWhiteSpace(m.Email)) return Fail(m, generic);
                if (!string.Equals((m.Email ?? "").Trim(), _opt.AdminEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogWarning("Admin login rejected: email does not match configured admin account");
                    return Fail(m, generic);
                }
                user = await _users.FindByEmailAsync(m.Email!);
                if (user == null) return Fail(m, generic);
            }
            else if (role == "student")
            {
                var s = await _db.Students.FirstOrDefaultAsync(x => x.StudentID == m.InstitutionId);
                if (s == null || !s.IsActive) return Fail(m, generic);
                if (string.IsNullOrWhiteSpace(s.Email)) return Fail(m, generic);
                if (string.IsNullOrWhiteSpace(m.Email)) m.Email = s.Email;
                if (!string.Equals(s.Email, m.Email, StringComparison.OrdinalIgnoreCase)) return Fail(m, generic);
                if (s.IdentityUserId != null) user = await _users.FindByIdAsync(s.IdentityUserId);
                if (user == null) user = await _users.FindByEmailAsync(s.Email);
                if (user == null) return Fail(m, generic);
            }
            else if (role == "faculty")
            {
                var f = await _db.Faculty.FirstOrDefaultAsync(x => x.FacultyID == m.InstitutionId);
                if (f == null || !f.IsActive) return Fail(m, generic);
                if (string.IsNullOrWhiteSpace(f.Email)) return Fail(m, generic);
                if (string.IsNullOrWhiteSpace(m.Email)) m.Email = f.Email;
                if (!string.Equals(f.Email, m.Email, StringComparison.OrdinalIgnoreCase)) return Fail(m, generic);
                if (f.IdentityUserId != null) user = await _users.FindByIdAsync(f.IdentityUserId);
                if (user == null) user = await _users.FindByEmailAsync(f.Email);
                if (user == null) return Fail(m, generic);
            }

            if (user == null) return Fail(m, generic);

            if (await _users.IsLockedOutAsync(user)) return Fail(m, "Account is locked due to too many failed attempts. Please contact the administrator.");

            bool roleOk = role == "admin" ? await _users.IsInRoleAsync(user, RoleNames.Admin)
                        : role == "faculty" ? await _users.IsInRoleAsync(user, RoleNames.Faculty)
                        : await _users.IsInRoleAsync(user, RoleNames.Student);

            bool pwdOk = await _users.CheckPasswordAsync(user, m.Password);
            if (!pwdOk || !roleOk)
            {
                await _users.AccessFailedAsync(user);
                return Fail(m, generic);
            }

            await _users.ResetAccessFailedCountAsync(user);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user);
            await _signIn.SignInAsync(user, m.RememberMe);
            await _audit.LogAsync("Login", "Account", user.Email, actorName: user.Email, actorId: user.Id, details: role);

            if (!string.IsNullOrEmpty(m.ReturnUrl) && Url.IsLocalUrl(m.ReturnUrl)) return Redirect(m.ReturnUrl);
            return role == "student" ? RedirectToAction("Index", "Student") : RedirectToAction("Index", "Dashboard");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Signin unhandled exception for role={Role}, InstitutionId={InstitutionId}", m.Role, m.InstitutionId);
            return Fail(m, "Login failed. Please try again.");
        }
    }

    private IActionResult Fail(RoleLoginViewModel m, string message)
    {
        ModelState.AddModelError("", message);
        m.Password = "";
        return View("RoleLogin", m);
    }

    [AllowAnonymous, HttpGet("account/lookup")]
    public async Task<IActionResult> Lookup(string role, string id, string? email)
    {
        if (string.IsNullOrWhiteSpace(role)) return Json(new { email = "", institutionId = "" });
        id = (id?.Trim() ?? "").ToUpperInvariant();
        email = email?.Trim() ?? "";

        if (role == "student")
        {
            if (!string.IsNullOrEmpty(email) && string.IsNullOrWhiteSpace(id)) id = EmailHelper.NormalizeToId(email);
            var studentRec = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.StudentID == id);
            if (studentRec != null && studentRec.IsActive)
            {
                return Json(new { email = studentRec.Email, institutionId = studentRec.StudentID });
            }
            return Json(new { email = "", institutionId = id });
        }
        else if (role == "faculty")
        {
            if (!string.IsNullOrEmpty(email) && string.IsNullOrWhiteSpace(id)) id = EmailHelper.NormalizeToId(email);
            var facultyRec = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.FacultyID == id);
            if (facultyRec != null && facultyRec.IsActive)
            {
                return Json(new { email = facultyRec.Email, institutionId = facultyRec.FacultyID });
            }
            return Json(new { email = "", institutionId = id });
        }
        return Json(new { email = "", institutionId = "" });
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Logout", "Account", User.Identity?.Name);
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet] public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel m)
    {
        if (!ModelState.IsValid) return View(m);
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToAction("Index", "Home");
        var res = await _users.ChangePasswordAsync(user, m.CurrentPassword, m.NewPassword);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(m);
        }
        user.MustChangePassword = false;
        await _users.UpdateAsync(user);
        await _signIn.RefreshSignInAsync(user);
        await _audit.LogAsync("PasswordChanged", "Account", user.Email);
        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous] public IActionResult AccessDenied() => View();
}
