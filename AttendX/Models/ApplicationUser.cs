using Microsoft.AspNetCore.Identity;

namespace AttendX.Models;

/// <summary>Login account (Admin, HOD, Faculty, Student). All roles sign in via Identity.</summary>
public class ApplicationUser : IdentityUser
{
    [StringLength(150)]
    public string FullName { get; set; } = "";

    /// <summary>When true the user is forced to change the password on next login.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
