namespace AttendX.Services;

public interface IStudentIdService
{
    Task<string> GenerateAsync(int semester, int sectionId, int branchId);
}

/// <summary>
/// Format: [Semester][Section]-[BranchCode]-[001]   e.g. 1EV1-CSE-001, 3EC2-ECE-014.
/// The highest ACTIVE sequence for the same semester + section + branch is found and incremented.
/// </summary>
public class StudentIdService(AppDbContext db) : IStudentIdService
{
    private readonly AppDbContext _db = db;

    public static string Prefix(int semester, string sectionName, string branchCode)
    {
        if (!string.IsNullOrEmpty(sectionName) && sectionName.Length > 0 && sectionName[0] - '0' == semester)
            return $"{sectionName}-{branchCode}-";
        return $"{semester}{sectionName}-{branchCode}-";
    }

    /// <summary>Pure function: next sequence number given the existing IDs that start with the prefix.</summary>
    public static int NextSequence(IEnumerable<string> existingIds, string prefix)
    {
        int max = 0;
        foreach (var id in existingIds)
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(id.AsSpan(prefix.Length), out var n) && n > max) max = n;
        }
        return max + 1;
    }

    public static string Format(string prefix, int sequence) => prefix + sequence.ToString("000");

    public async Task<string> GenerateAsync(int semester, int sectionId, int branchId)
    {
        var section = await _db.Sections.AsNoTracking().FirstAsync(s => s.Id == sectionId);
        var branch = await _db.Branches.AsNoTracking().FirstAsync(b => b.Id == branchId);
        var prefix = Prefix(semester, section.Name, branch.Code);
        var ids = await _db.Students.AsNoTracking()
            .Where(s => s.IsActive && s.StudentID.StartsWith(prefix))
            .Select(s => s.StudentID).ToListAsync();
        return Format(prefix, NextSequence(ids, prefix));
    }
}
