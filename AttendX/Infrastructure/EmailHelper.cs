namespace AttendX.Infrastructure;

/// <summary>ID-based email helper: ID.ToLower() + "@attendx.edu.in" — used everywhere for display and login.</summary>
public static class EmailHelper
{
    public static string FromId(string? id, string? fullName = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        string cleanId = id.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fullName)) return cleanId + "@attendx.edu.in";
        string cleanName = string.Concat(fullName.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch == ' ' ? ch : ' ')).Trim().Replace(" ", ".");
        return cleanName + "." + cleanId + "@attendx.edu.in";
    }

    public static string StudentEmail(string? studentId, string? fullName = null) => FromId(studentId, fullName);

    public static string FacultyEmail(string? facultyId, string? fullName = null) => FromId(facultyId, fullName);

    /// <summary>Extract ID part from email or raw ID input. Returns uppercase ID for DB lookup.</summary>
    public static string NormalizeToId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.Trim();
        if (input.Contains('@'))
        {
            var at = input.IndexOf('@');
            var local = input.Substring(0, at).Trim();
            if (local.Contains('.'))
            {
                var tail = local.Substring(local.LastIndexOf('.') + 1).Trim();
                if (!string.IsNullOrWhiteSpace(tail)) return tail.ToUpperInvariant();
            }
            return local.ToUpperInvariant();
        }
        return input.Trim().ToUpperInvariant();
    }

    public static bool IsAttendXEmail(string? email) => !string.IsNullOrWhiteSpace(email) && email.Trim().EndsWith("@attendx.edu.in", StringComparison.OrdinalIgnoreCase);
}
