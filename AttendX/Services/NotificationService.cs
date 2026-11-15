using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface INotificationService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    Task SendSmsAsync(string to, string message);
    Task SendAbsenceAlertAsync(int studentId, DateTime date, string subjectCode);
    Task SendLowAttendanceAlertAsync(int studentId, decimal percent, decimal threshold);
    /// <summary>Sweep all students below threshold and send one warning per student per day (deduplicated).</summary>
    Task<int> SendLowAttendanceSweepAsync();
}

/// <summary>
/// Outbound notification service. Provider-agnostic:
///   Provider="Log" (default) - logs to Serilog AND writes to NotificationLog (no external calls).
///   Provider="Smtp"          - System.Net.Mail (SMTP server).
///   Provider="SendGrid"      - SendGrid via SMTP relay (SMTP host configurable as smtp.sendgrid.net).
/// SMS   Provider="Log"       - logs only; Provider="Twilio" - Twilio REST API.
/// Every attempt (sent/failed) is persisted to NotificationLogs for auditability.
/// </summary>
public class NotificationService(AppDbContext db, IOptions<AttendXOptions> o, IOptions<NotificationOptions> n, ILogger<NotificationService> log) : INotificationService
{
    private readonly AppDbContext _db = db;
    private readonly AttendXOptions _o = o.Value;
    private readonly NotificationOptions _n = n.Value;
    private readonly ILogger<NotificationService> _log = log;

    private bool EmailEnabled => _n.Provider is "Smtp" or "SendGrid" && !string.IsNullOrWhiteSpace(FromEmail);
    private bool SmsEnabled => _n.Twilio != null && !string.IsNullOrWhiteSpace(_n.Twilio.AccountSid) && !string.IsNullOrWhiteSpace(_n.Twilio.AuthToken) && !string.IsNullOrWhiteSpace(_n.Twilio.FromNumber);
    private string FromEmail => _n.Provider == "Smtp" ? (_n.Smtp?.FromEmail ?? "noreply@attendx.edu.in") : (_n.SendGrid?.FromEmail ?? "noreply@attendx.edu.in");
    private string FromName => _n.Provider == "Smtp" ? (_n.Smtp?.FromName ?? "AttendX") : (_n.SendGrid?.FromName ?? "AttendX");

    // ------------------------------------------------------------------ email
    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        var ok = await TrySendEmailAsync(to, subject, body, isHtml, "Notification", null);
        if (!ok) _log.LogWarning("Email to {To} stored in NotificationLog with status Failed.", to);
    }

    private async Task<bool> TrySendEmailAsync(string to, string subject, string body, bool isHtml, string entityType, string? entityId)
    {
        var entry = new NotificationLog
        {
            CreatedAt = DateTime.UtcNow, Channel = "Email", To = to, Subject = Truncate(subject, 200),
            Body = Truncate(body, 2000), Status = "Pending", EntityType = entityType, EntityId = entityId
        };
        _db.NotificationLogs.Add(entry);

        try
        {
            if (EmailEnabled)
            {
                using var msg = new MailMessage(FromEmail, to, subject, body) { IsBodyHtml = isHtml, SubjectEncoding = System.Text.Encoding.UTF8 };
                using var smtp = new SmtpClient();
                if (_n.Provider == "SendGrid")
                {
                    smtp.Host = "smtp.sendgrid.net"; smtp.Port = 587; smtp.EnableSsl = true;
                    smtp.Credentials = new NetworkCredential("apikey", _n.SendGrid!.ApiKey);
                }
                else // Smtp
                {
                    smtp.Host = _n.Smtp!.Host; smtp.Port = _n.Smtp.Port; smtp.EnableSsl = _n.Smtp.EnableSsl;
                    smtp.Credentials = string.IsNullOrWhiteSpace(_n.Smtp.User) ? null : new NetworkCredential(_n.Smtp.User, _n.Smtp.Password);
                }
                await smtp.SendMailAsync(msg);
                entry.Status = "Sent"; entry.SentAt = DateTime.UtcNow;
            }
            else
            {
                entry.Status = "Logged"; entry.SentAt = DateTime.UtcNow;
                _log.LogInformation("EMAIL [provider=Log] to {To}: Subject={Subject}", to, subject);
            }
        }
        catch (Exception ex)
        {
            entry.Status = "Failed"; entry.Error = Truncate(ex.Message, 500);
            _log.LogError(ex, "Email send failed to {To}", to);
        }
        await _db.SaveChangesAsync();
        return entry.Status is "Sent" or "Logged";
    }

    // ------------------------------------------------------------------ sms
    public async Task SendSmsAsync(string to, string message)
    {
        var ok = await TrySendSmsAsync(to, message, "Notification", null);
        if (!ok) _log.LogWarning("SMS to {To} stored in NotificationLog with status Failed.", to);
    }

    private async Task<bool> TrySendSmsAsync(string to, string message, string entityType, string? entityId)
    {
        var entry = new NotificationLog
        {
            CreatedAt = DateTime.UtcNow, Channel = "Sms", To = to, Subject = Truncate(message, 200),
            Body = Truncate(message, 2000), Status = "Pending", EntityType = entityType, EntityId = entityId
        };
        _db.NotificationLogs.Add(entry);

        try
        {
            if (SmsEnabled)
            {
                using var http = new HttpClient { BaseAddress = new Uri("https://api.twilio.com/2010-04-01/Accounts/") };
                var accountSid = _n.Twilio!.AccountSid;
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = to, ["From"] = _n.Twilio.FromNumber, ["Body"] = message
                });
                var sidToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{accountSid}:{_n.Twilio.AuthToken}"));
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{accountSid}/Messages.json") { Content = content };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", sidToken);
                var resp = await http.SendAsync(req);
                if (resp.IsSuccessStatusCode) { entry.Status = "Sent"; entry.SentAt = DateTime.UtcNow; }
                else { entry.Status = "Failed"; entry.Error = Truncate(await resp.Content.ReadAsStringAsync(), 500); }
            }
            else
            {
                entry.Status = "Logged"; entry.SentAt = DateTime.UtcNow;
                _log.LogInformation("SMS [provider=Log] to {To}: {Message}", to, message);
            }
        }
        catch (Exception ex)
        {
            entry.Status = "Failed"; entry.Error = Truncate(ex.Message, 500);
            _log.LogError(ex, "SMS send failed to {To}", to);
        }
        await _db.SaveChangesAsync();
        return entry.Status is "Sent" or "Logged";
    }

    // ------------------------------------------------------------------ absence / low attendance triggers
    public async Task SendAbsenceAlertAsync(int studentId, DateTime date, string subjectCode)
    {
        var student = await _db.Students.AsNoTracking().Include(s => s.Branch).FirstOrDefaultAsync(s => s.Id == studentId);
        if (student == null) return;

        await TrySendEmailAsync(student.Email, "Absence Alert",
            $"You were marked absent for {subjectCode} on {date:dd MMM yyyy}.",
            false, "Student", studentId.ToString());

        var parent = await _db.Parents.AsNoTracking().FirstOrDefaultAsync(p => p.StudentId == studentId && p.IsActive);
        if (parent != null)
        {
            await TrySendEmailAsync(parent.Email, "Absence Alert - Your Ward",
                $"Your ward {student.FullName} ({student.StudentID}) was marked absent for {subjectCode} on {date:dd MMM yyyy}.",
                false, "Student", studentId.ToString());
            await TrySendSmsAsync(parent.Phone,
                $"AttendX Alert: {student.FullName} absent for {subjectCode} on {date:dd MMM yyyy}.",
                "Student", studentId.ToString());
        }
    }

    public async Task SendLowAttendanceAlertAsync(int studentId, decimal percent, decimal threshold)
    {
        if (studentId <= 0 || percent <= 0) return;   // legacy placeholder call: do nothing
        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId);
        if (student == null) return;

        // De-duplication: only one low-attendance email per student per day
        var today = DateTime.UtcNow.Date;
        bool alreadySentToday = await _db.NotificationLogs.AsNoTracking()
            .AnyAsync(n => n.EntityType == "Student" && n.EntityId == studentId.ToString()
                        && n.Subject == "Low Attendance Warning" && n.CreatedAt >= today);
        if (alreadySentToday) return;

        await TrySendEmailAsync(student.Email, "Low Attendance Warning",
            $"Your attendance is {percent}%, which is below the required {threshold}%.",
            false, "Student", studentId.ToString());

        var parent = await _db.Parents.AsNoTracking().FirstOrDefaultAsync(p => p.StudentId == studentId && p.IsActive);
        if (parent != null)
        {
            await TrySendEmailAsync(parent.Email, "Low Attendance Warning - Your Ward",
                $"Your ward {student.FullName} ({student.StudentID}) has {percent}% attendance, below the required {threshold}%.",
                false, "Student", studentId.ToString());
            await TrySendSmsAsync(parent.Phone,
                $"AttendX Alert: {student.FullName} attendance {percent}% below required {threshold}%.",
                "Student", studentId.ToString());
        }
    }

    /// <summary>Called by the daily Hangfire job: sweep students below threshold (once per day each).</summary>
    public async Task<int> SendLowAttendanceSweepAsync()
    {
        var settings = await _db.AttendanceSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        decimal threshold = settings?.DefaultThreshold ?? _o.DefaultThreshold;
        var today = DateTime.UtcNow.Date;

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync();
        int sent = 0;
        foreach (var sid in students)
        {
            // Compute attendance for the current academic year's date range (Aug 1 2026 - today)
            var from = new DateTime(2026, 8, 1);
            var rows = await _db.Attendance.AsNoTracking()
                .Where(a => a.StudentId == sid && a.Date >= from && a.Date <= today)
                .ToListAsync();
            if (rows.Count == 0) continue;
            var present = rows.Count(a => a.Status == AttendanceStatus.Present);
            var late = rows.Count(a => a.Status == AttendanceStatus.Late);
            var percent = AttendanceMath.Percent(present, late, rows.Count, settings?.LateWeight ?? 1m);
            if (percent < threshold)
            {
                await SendLowAttendanceAlertAsync(sid, percent, threshold);
                sent++;
            }
        }
        _log.LogInformation("Deficiency sweep complete: {Sent} low-attendance alerts queued/sent.", sent);
        return sent;
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);
}
