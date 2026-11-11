using ClosedXML.Excel;
using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface IMasterDataExportService
{
    Task<byte[]> BuildAsync();
    Task<string> SaveToFileAsync(string path);
}

/// <summary>Writes ALL master data (admin, faculty, students, subjects, timetable, holidays, notices) to one Excel workbook.</summary>
public class MasterDataExportService(AppDbContext db, IOptions<AttendXOptions> o) : IMasterDataExportService
{
    private readonly AppDbContext _db = db;
    private readonly AttendXOptions _o = o.Value;

    private static void Header(IXLWorksheet ws, params string[] cols)
    {
        for (int i = 0; i < cols.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = cols[i];
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
        }
        ws.SheetView.FreezeRows(1);
    }

    private static void Finish(IXLWorksheet ws) { ws.Columns().AdjustToContents(1, 200); }

    public async Task<byte[]> BuildAsync()
    {
        _db.Database.SetCommandTimeout(300);
        try
        {
        using var wb = new XLWorkbook();

        var info = wb.Worksheets.Add("README");
        info.Cell(1, 1).Value = "AttendX master data (exported " + DateTime.UtcNow.ToString("dd MMM yyyy HH:mm") + ")";
        info.Cell(2, 1).Value = "Passwords are never exported. The 'Default password' columns have been removed for security.";
        info.Cell(3, 1).Value = "Regenerate any time: Admin > Backup / Restore > Download master data (Excel).";
        info.Column(1).Width = 120;

        var adminRole = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == RoleNames.Admin);
        var admins = adminRole == null ? new List<ApplicationUser>()
            : await _db.Users.AsNoTracking().Where(u => _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRole.Id)).ToListAsync();
        var wsA = wb.Worksheets.Add("Admin"); Header(wsA, "Email", "Full name", "Last login");
        int r = 2;
        foreach (var a in admins) { wsA.Cell(r, 1).Value = a.Email ?? ""; wsA.Cell(r, 2).Value = a.FullName; wsA.Cell(r, 3).Value = a.LastLoginAt?.ToString("dd MMM yyyy HH:mm") ?? ""; r++; }
        Finish(wsA);

        var wsF = wb.Worksheets.Add("Faculty"); Header(wsF, "Institution ID", "Full name", "Email", "Phone", "Department", "Designation", "Is HOD", "Active");
        r = 2;
        foreach (var f in await _db.Faculty.AsNoTracking().Include(x => x.Branch).OrderBy(x => x.FacultyID).ToListAsync())
        {
            wsF.Cell(r, 1).Value = f.FacultyID; wsF.Cell(r, 2).Value = f.FullName; wsF.Cell(r, 3).Value = f.Email; wsF.Cell(r, 4).Value = f.Phone;
            wsF.Cell(r, 5).Value = f.Branch?.Code ?? f.Department; wsF.Cell(r, 6).Value = f.Designation; wsF.Cell(r, 7).Value = f.IsHOD ? "Yes" : "No"; wsF.Cell(r, 8).Value = f.IsActive ? "Yes" : "No";
            r++;
        }
        Finish(wsF);

        var wsS = wb.Worksheets.Add("Students"); Header(wsS, "Institution ID", "Full name", "Email", "Phone", "Branch", "Section", "Semester", "Year", "Joining date", "Active");
        r = 2;
        foreach (var s in await _db.Students.AsNoTracking().Include(x => x.Branch).Include(x => x.Section).OrderBy(x => (x.Branch != null ? x.Branch.Code : "")).ThenBy(x => x.Semester).ThenBy(x => x.StudentID).ToListAsync())
        {
            wsS.Cell(r, 1).Value = s.StudentID; wsS.Cell(r, 2).Value = s.FullName; wsS.Cell(r, 3).Value = s.Email; wsS.Cell(r, 4).Value = s.Phone;
            wsS.Cell(r, 5).Value = s.Branch?.Code ?? ""; wsS.Cell(r, 6).Value = s.Section?.Name ?? ""; wsS.Cell(r, 7).Value = s.Semester; wsS.Cell(r, 8).Value = s.Year;
            wsS.Cell(r, 9).Value = s.JoiningDate.ToString("yyyy-MM-dd"); wsS.Cell(r, 10).Value = s.IsActive ? "Yes" : "No";
            r++;
        }
        Finish(wsS);

        var wsU = wb.Worksheets.Add("Subjects"); Header(wsU, "Branch", "Semester", "Subject code", "Subject name", "Credits");
        r = 2;
        foreach (var s in await _db.Subjects.AsNoTracking().Include(x => x.Branch).OrderBy(x => (x.Branch != null ? x.Branch.Code : "")).ThenBy(x => x.Semester).ThenBy(x => x.SubjectCode).ToListAsync())
        { wsU.Cell(r, 1).Value = s.Branch?.Code ?? ""; wsU.Cell(r, 2).Value = s.Semester; wsU.Cell(r, 3).Value = s.SubjectCode; wsU.Cell(r, 4).Value = s.SubjectName; wsU.Cell(r, 5).Value = s.Credits; r++; }
        Finish(wsU);

        var wsT = wb.Worksheets.Add("Timetable"); Header(wsT, "Branch", "Section", "Semester", "Day", "Period", "Start", "End", "Subject code", "Subject", "Faculty ID", "Faculty", "Room");
        r = 2;
        var tts = await _db.Timetables.AsNoTracking().Include(x => x.Branch).Include(x => x.Section).Include(x => x.Subject).Include(x => x.Faculty).Where(x => x.IsActive)
            .OrderBy(x => (x.Branch != null ? x.Branch.Code : "")).ThenBy(x => x.Semester).ThenBy(x => (x.Section != null ? x.Section.Name : "")).ThenBy(x => x.Day).ThenBy(x => x.Period).ToListAsync();
        foreach (var t in tts)
        {
            wsT.Cell(r, 1).Value = t.Branch?.Code ?? ""; wsT.Cell(r, 2).Value = t.Section?.Name ?? ""; wsT.Cell(r, 3).Value = t.Semester; wsT.Cell(r, 4).Value = t.Day.ToString(); wsT.Cell(r, 5).Value = t.Period;
            wsT.Cell(r, 6).Value = t.StartTime.ToString(@"hh\:mm"); wsT.Cell(r, 7).Value = t.EndTime.ToString(@"hh\:mm");
            wsT.Cell(r, 8).Value = t.Subject?.SubjectCode ?? ""; wsT.Cell(r, 9).Value = t.Subject?.SubjectName ?? ""; wsT.Cell(r, 10).Value = t.Faculty?.FacultyID ?? ""; wsT.Cell(r, 11).Value = t.Faculty?.FullName ?? ""; wsT.Cell(r, 12).Value = t.RoomNo;
            r++;
        }
        Finish(wsT);

        var wsH = wb.Worksheets.Add("Holidays"); Header(wsH, "Date", "Name", "Type");
        r = 2;
        foreach (var h in await _db.Holidays.AsNoTracking().OrderBy(x => x.Date).ToListAsync()) { wsH.Cell(r, 1).Value = h.Date.ToString("yyyy-MM-dd"); wsH.Cell(r, 2).Value = h.Name; wsH.Cell(r, 3).Value = h.Type; r++; }
        Finish(wsH);
        var wsN = wb.Worksheets.Add("Notices"); Header(wsN, "Published", "Audience", "Title", "Body");
        r = 2;
        foreach (var n in await _db.Notices.AsNoTracking().OrderByDescending(x => x.PublishedAt).ToListAsync()) { wsN.Cell(r, 1).Value = n.PublishedAt.ToString("yyyy-MM-dd HH:mm"); wsN.Cell(r, 2).Value = n.Audience; wsN.Cell(r, 3).Value = n.Title; wsN.Cell(r, 4).Value = n.Body; r++; }
        Finish(wsN);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
        }
        finally { _db.Database.SetCommandTimeout(120); }
    }

    public async Task<string> SaveToFileAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, await BuildAsync());
        return path;
    }
}
