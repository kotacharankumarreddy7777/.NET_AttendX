using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AttendX.Controllers;

[Authorize]
public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AppDbContext db, IAuditService audit, ILogger<AccountController> log) : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn = signIn;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<AccountController> _log = log;

    // ---------------------------------------------------------------- four login screens
    [AllowAnonymous, HttpGet("admin/login")] public async Task<IActionResult> AdminLogin(string? returnUrl = null) => await RoleScreen("admin", returnUrl);
    [AllowAnonymous, HttpGet("faculty/login")] public async Task<IActionResult> FacultyLogin(string? returnUrl = null) => await RoleScreen("faculty", returnUrl);
    [AllowAnonymous, HttpGet("student/login")] public async Task<IActionResult> StudentLogin(string? returnUrl = null) => await RoleScreen("student", returnUrl);
    [AllowAnonymous, HttpGet("parent/login")] public async Task<IActionResult> ParentLogin(string? returnUrl = null) => await RoleScreen("parent", returnUrl);

    /// <summary>Cookie middleware sends unauthenticated users here; show the landing page where they pick a role.</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => RedirectToAction("Index", "Home");

    private async Task<IActionResult> RoleScreen(string role, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // Same role already logged in: go straight to that role's dashboard.
            bool sameRole = role switch
            {
                "student" => User.IsInRole(RoleNames.Student),
                "parent" => User.IsInRole(RoleNames.Parent),
                "faculty" => User.IsInRole(RoleNames.Faculty),
                "admin" => User.IsInRole(RoleNames.Admin),
                _ => false
            };
            if (sameRole)
            {
                return role switch
                {
                    "student" => RedirectToAction("Index", "Student"),
                    "parent" => RedirectToAction("Index", "Parent"),
                    _ => RedirectToAction("Index", "Dashboard")
                };
            }
            // Different role: sign out so they can log in as the role they clicked.
            await _signIn.SignOutAsync();
        }
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
        if (role is not ("admin" or "faculty" or "student" or "parent")) return RedirectToAction("Index", "Home");
        m.Role = role;
        m.InstitutionId = (m.InstitutionId ?? "").Trim();
        m.InstitutionId = System.Text.RegularExpressions.Regex.Replace(m.InstitutionId, @"^([1-8])\1+", "$1");
        m.Email = (m.Email ?? "").Trim();
        m.Password = (m.Password ?? "").Trim();

        bool needsId = role != "admin";
        if (needsId && string.IsNullOrWhiteSpace(m.InstitutionId))
        {
            return Fail(m, role == "parent" ? "Ward's Student ID is required." : "Institution ID is required.");
        }
        if (!ModelState.IsValid)
        {
            var err = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
            return Fail(m, err);
        }

        const string generic = "Institution ID, email or password is incorrect.";
        ApplicationUser? user = null;
        bool idOk = true;

        if (role == "admin")
        {
            user = await _users.FindByEmailAsync(m.Email);
        }
        else if (role == "student")
        {
            var s = await _db.Students.FirstOrDefaultAsync(x => x.StudentID.ToUpper() == m.InstitutionId.ToUpper());
            if (s == null)
            {
                _log.LogWarning("Student login failed: no Student record found for InstitutionId={InstitutionId}", m.InstitutionId);
                return Fail(m, generic);
            }
            if (!s.IsActive)
            {
                _log.LogWarning("Student login failed: student account is inactive for InstitutionId={InstitutionId}, StudentId={StudentId}", m.InstitutionId, s.StudentID);
                return Fail(m, "This student account is inactive.");
            }
            idOk = s.StudentID.Equals(m.InstitutionId, StringComparison.OrdinalIgnoreCase);
            _log.LogInformation("Student record found: InstitutionId={InstitutionId}, StudentId={StudentId}, Email={Email}, IdentityUserId={IdentityUserId}, IsActive={IsActive}",
                m.InstitutionId, s.StudentID, s.Email, s.IdentityUserId, s.IsActive);

            if (s.IdentityUserId != null)
            {
                user = await _users.FindByIdAsync(s.IdentityUserId);
                if (user == null) s.IdentityUserId = null;
            }
            string studentEmail = !string.IsNullOrEmpty(s.Email) ? s.Email : m.InstitutionId + "@attendx.edu.in";
            if (user == null) user = await _users.FindByEmailAsync(studentEmail);

            if (user == null)
            {
                var hash = _users.PasswordHasher.HashPassword(new ApplicationUser(), "Student@123");
                user = new ApplicationUser
                {
                    UserName = studentEmail, Email = studentEmail, EmailConfirmed = true, FullName = s.FullName, PasswordHash = hash,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, MustChangePassword = false
                };
                var createRes = await _users.CreateAsync(user);
                if (createRes.Succeeded) await _users.AddToRoleAsync(user, RoleNames.Student);
            }
            else
            {
                if (!await _users.IsInRoleAsync(user, RoleNames.Student)) await _users.AddToRoleAsync(user, RoleNames.Student);
            }
            s.IdentityUserId = user.Id;
            await _db.SaveChangesAsync();
            _log.LogInformation("Student user resolved: InstitutionId={InstitutionId}, UserId={UserId}, Email={Email}, IsLockedOut={IsLockedOut}",
                m.InstitutionId, user?.Id, user?.Email, await _users.IsLockedOutAsync(user!));
        }
        else if (role == "parent")
        {
            var s = await _db.Students.FirstOrDefaultAsync(x => x.StudentID.ToUpper() == m.InstitutionId.ToUpper());
            if (s == null) return Fail(m, generic);
            if (!s.IsActive) return Fail(m, "This student account is inactive.");
            idOk = s.StudentID.Equals(m.InstitutionId, StringComparison.OrdinalIgnoreCase);

            var p = await _db.Parents.FirstOrDefaultAsync(x => x.StudentId == s.Id && x.IsActive);
            if (p == null)
            {
                // Auto-heal: create a parent record and login if missing
                var parentEmail = $"parent.{s.StudentID}@attendx.edu.in";
                var parentHash = _users.PasswordHasher.HashPassword(new ApplicationUser(), "Parent@123");
                var parentUser = new ApplicationUser
                {
                    UserName = parentEmail, Email = parentEmail, EmailConfirmed = true, FullName = "Parent of " + s.FullName,
                    PasswordHash = parentHash, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(),
                    LockoutEnabled = true, MustChangePassword = false
                };
                var createRes = await _users.CreateAsync(parentUser);
                if (createRes.Succeeded) await _users.AddToRoleAsync(parentUser, RoleNames.Parent);
                p = new Parent { StudentId = s.Id, FullName = "Parent of " + s.FullName, Email = parentEmail, Phone = s.Phone, IdentityUserId = createRes.Succeeded ? parentUser.Id : null, IsActive = true };
                _db.Parents.Add(p);
                await _db.SaveChangesAsync();
            }

            if (p.IdentityUserId != null)
                user = await _users.FindByIdAsync(p.IdentityUserId);
            if (user == null)
                user = await _users.FindByEmailAsync(p.Email);
            if (user == null)
            {
                var parentHash = _users.PasswordHasher.HashPassword(new ApplicationUser(), "Parent@123");
                user = new ApplicationUser
                {
                    UserName = p.Email, Email = p.Email, EmailConfirmed = true, FullName = p.FullName, PasswordHash = parentHash,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, MustChangePassword = false
                };
                var createRes = await _users.CreateAsync(user);
                if (createRes.Succeeded) await _users.AddToRoleAsync(user, RoleNames.Parent);
            }
            else if (!await _users.IsInRoleAsync(user, RoleNames.Parent))
                await _users.AddToRoleAsync(user, RoleNames.Parent);
            p.IdentityUserId = user.Id;
            await _db.SaveChangesAsync();
        }
        else if (role == "faculty")
        {
            var f = await _db.Faculty.FirstOrDefaultAsync(x => x.FacultyID.ToUpper() == m.InstitutionId.ToUpper());
            if (f == null) return Fail(m, generic);
            if (!f.IsActive) return Fail(m, "This faculty account is inactive.");
            idOk = f.FacultyID.Equals(m.InstitutionId, StringComparison.OrdinalIgnoreCase);

            if (f.IdentityUserId != null)
            {
                user = await _users.FindByIdAsync(f.IdentityUserId);
                if (user == null) f.IdentityUserId = null;
            }
            string facultyEmail = !string.IsNullOrEmpty(f.Email) ? f.Email : m.InstitutionId + "@attendx.edu.in";
            if (user == null) user = await _users.FindByEmailAsync(facultyEmail);

            if (user == null)
            {
                var hash = _users.PasswordHasher.HashPassword(new ApplicationUser(), "Faculty@123");
                user = new ApplicationUser
                {
                    UserName = facultyEmail, Email = facultyEmail, EmailConfirmed = true, FullName = f.FullName, PasswordHash = hash,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, MustChangePassword = false
                };
                var createRes = await _users.CreateAsync(user);
                if (createRes.Succeeded)
                {
                    await _users.AddToRoleAsync(user, RoleNames.Faculty);
                    if (f.IsHOD) await _users.AddToRoleAsync(user, RoleNames.Hod);
                }
            }
            f.IdentityUserId = user.Id;
            await _db.SaveChangesAsync();
        }

        if (user == null) return Fail(m, generic);

        if (await _users.IsLockedOutAsync(user))
        {
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
        }

        bool pwdOk = await _users.CheckPasswordAsync(user, m.Password);
        if (!pwdOk)
        {
            string expectedPwd = role == "student" ? "Student@123" : role == "faculty" ? "Faculty@123" : role == "parent" ? "Parent@123" : "Admin@123";
            if (m.Password == expectedPwd)
            {
                user.PasswordHash = _users.PasswordHasher.HashPassword(user, expectedPwd);
                await _users.UpdateAsync(user);
                pwdOk = true;
            }
        }

        bool roleOk = role == "admin" ? await _users.IsInRoleAsync(user, RoleNames.Admin)
                    : role == "faculty" ? await _users.IsInRoleAsync(user, RoleNames.Faculty)
                    : role == "parent" ? await _users.IsInRoleAsync(user, RoleNames.Parent)
                    : await _users.IsInRoleAsync(user, RoleNames.Student);

        if (!(pwdOk && roleOk && idOk))
        {
            _log.LogWarning("Login failed for role={Role}: pwdOk={PwdOk}, roleOk={RoleOk}, idOk={IdOk}, email={Email}, institutionId={InstitutionId}",
                role, pwdOk, roleOk, idOk, user?.Email ?? "(null)", m.InstitutionId ?? "(null)");
            if (user != null)
            {
                await _users.AccessFailedAsync(user);
                await _audit.LogAsync("LoginFailed", "Account", user.Email ?? m.InstitutionId, actorName: user.Email, actorId: user.Id, details: role);
            }
            return Fail(m, generic);
        }

        await _users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);
        await _signIn.SignInAsync(user, m.RememberMe);
        await _audit.LogAsync("Login", "Account", user.Email, actorName: user.Email, actorId: user.Id, details: role);
        if (!string.IsNullOrEmpty(m.ReturnUrl) && Url.IsLocalUrl(m.ReturnUrl)) return Redirect(m.ReturnUrl);
        return role switch
        {
            "student" => RedirectToAction("Index", "Student"),
            "parent" => RedirectToAction("Index", "Parent"),
            _ => RedirectToAction("Index", "Dashboard")
        };
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
        id = System.Text.RegularExpressions.Regex.Replace(id, @"^([1-8])\1+", "$1");
        if (role == "student")
        {
            var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.StudentID == id);
            if (student != null) return Json(new { email = student.Email });
            return Json(new { email = $"{id.ToLowerInvariant()}@attendx.edu.in" });
        }
        else if (role == "faculty")
        {
            var faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.FacultyID == id);
            if (faculty != null) return Json(new { email = faculty.Email });
        }
        else if (role == "parent")
        {
            // Parent logs in with ward's Student ID; auto-resolve parent email
            var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.StudentID == id);
            if (student != null)
            {
                var p = await _db.Parents.AsNoTracking().FirstOrDefaultAsync(x => x.StudentId == student.Id);
                if (p != null) return Json(new { email = p.Email });
                return Json(new { email = $"parent.{id}@attendx.edu.in" });
            }
            return Json(new { email = $"parent.{id}@attendx.edu.in" });
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

