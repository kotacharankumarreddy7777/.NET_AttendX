using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace AttendX.Controllers;

[Authorize]
public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AppDbContext db, IAuditService audit) : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn = signIn;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;

    // ---------------------------------------------------------------- three login screens
    [AllowAnonymous, HttpGet("admin/login")] public IActionResult AdminLogin(string? returnUrl = null) => RoleScreen("admin", returnUrl);
    [AllowAnonymous, HttpGet("faculty/login")] public IActionResult FacultyLogin(string? returnUrl = null) => RoleScreen("faculty", returnUrl);
    [AllowAnonymous, HttpGet("student/login")] public IActionResult StudentLogin(string? returnUrl = null) => RoleScreen("student", returnUrl);

    /// <summary>Cookie middleware sends unauthenticated users here; show the landing page where they pick a role.</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => RedirectToAction("Index", "Home");

    private IActionResult RoleScreen(string role, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View("RoleLogin", new RoleLoginViewModel { Role = role, ReturnUrl = returnUrl });
    }

    /// <summary>
    /// Faculty/Student: Institution ID + email + password must ALL belong to the same account. Admin: email + password.
    /// Any mismatch gives the same generic message (no user enumeration) and counts as a failed attempt (lockout).
    /// </summary>
    [AllowAnonymous, HttpPost("account/signin")]
    public async Task<IActionResult> Signin(RoleLoginViewModel m)
    {
        var role = (m.Role ?? "").ToLowerInvariant();
        if (role is not ("admin" or "faculty" or "student")) return RedirectToAction("Index", "Home");
        m.Role = role;
        bool needsId = role != "admin";
        if (needsId)
        {
            if (string.IsNullOrWhiteSpace(m.InstitutionId)) ModelState.AddModelError(nameof(m.InstitutionId), "Institution ID is required.");
            else if (!Regex.IsMatch(m.InstitutionId.Trim(), @"^[A-Za-z0-9\-]{3,30}$")) ModelState.AddModelError(nameof(m.InstitutionId), "Institution ID may contain letters, digits and '-' only.");
        }
        if (!ModelState.IsValid) { m.Password = ""; return View("RoleLogin", m); }

        var email = m.Email.Trim();
        var instId = (m.InstitutionId ?? "").Trim();
        const string generic = "Institution ID, email or password is incorrect.";
        var user = await _users.FindByEmailAsync(email);
        if (user == null)
        {
            await _audit.LogAsync("LoginFailed", "Account", email, actorName: email, details: role);
            return Fail(m, generic);
        }
        if (await _users.IsLockedOutAsync(user)) return Fail(m, "Too many failed attempts. This account is locked for 10 minutes.");

        bool pwdOk = await _users.CheckPasswordAsync(user, m.Password);
        bool roleOk, idOk = true, active = true;
        if (role == "admin") roleOk = await _users.IsInRoleAsync(user, RoleNames.Admin);
        else if (role == "faculty")
        {
            roleOk = await _users.IsInRoleAsync(user, RoleNames.Faculty);
            var f = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.IdentityUserId == user.Id);
            idOk = f != null && f.FacultyID.Equals(instId, StringComparison.OrdinalIgnoreCase);
            active = f?.IsActive == true;
        }
        else
        {
            roleOk = await _users.IsInRoleAsync(user, RoleNames.Student);
            var s = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.IdentityUserId == user.Id);
            idOk = s != null && s.StudentID.Equals(instId, StringComparison.OrdinalIgnoreCase);
            active = s?.IsActive == true;
        }

        if (!(pwdOk && roleOk && idOk && active))
        {
            await _users.AccessFailedAsync(user);                                   // counts towards lockout
            await _audit.LogAsync("LoginFailed", "Account", email, actorName: email, actorId: user.Id, details: role);
            // only AFTER the password is proven correct do we reveal that the wrong screen was used
            return Fail(m, pwdOk && !roleOk ? $"This account is not a {role} account. Please use the correct login screen." : generic);
        }

        await _users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = DateTime.Now;
        await _users.UpdateAsync(user);
        await _signIn.SignInAsync(user, m.RememberMe);
        await _audit.LogAsync("Login", "Account", user.Email, actorName: user.Email, actorId: user.Id, details: role);
        if (!string.IsNullOrEmpty(m.ReturnUrl) && Url.IsLocalUrl(m.ReturnUrl)) return Redirect(m.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    private IActionResult Fail(RoleLoginViewModel m, string message)
    {
        ModelState.AddModelError("", message);
        m.Password = "";
        return View("RoleLogin", m);
    }

    [AllowAnonymous, HttpGet("account/lookup")]
    public async Task<IActionResult> Lookup(string role, string id)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(id)) return Json(new { email = "" });
        id = id.Trim();
        if (role == "student")
        {
            var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.StudentID == id);
            if (student != null) return Json(new { email = student.Email });
            return Json(new { email = $"{id}@attendx.edu.in" });
        }
        else if (role == "faculty")
        {
            var faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.FacultyID == id);
            if (faculty != null) return Json(new { email = faculty.Email });
        }
        return Json(new { email = "" });
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
        await _audit.LogAsync("PasswordChanged", "Account", user.Email);      // never log the password itself
        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous] public IActionResult AccessDenied() => View();
}
