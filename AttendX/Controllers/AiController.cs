using Microsoft.Extensions.Options;

namespace AttendX.Controllers;

public class AiChatRequest
{
    public string Message { get; set; } = "";
}

public class AiController(AppDbContext db, IAiService ai, ICurrentUser user, IOptions<AiOptions> aiOpts) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IAiService _ai = ai;
    private readonly ICurrentUser _user = user;
    private readonly AiOptions _opts = aiOpts.Value;

    [Authorize]
    public async Task<IActionResult> Index([FromQuery] bool partial = false, [FromQuery] string? q = null)
    {
        var role = User.IsInRole(RoleNames.Admin) ? "Admin"
                 : User.IsInRole(RoleNames.Hod) ? "HOD"
                 : User.IsInRole(RoleNames.Faculty) ? "Faculty"
                 : User.IsInRole(RoleNames.Student) ? "Student" : "User";

        if (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Hod))
        {
            var latestReport = await _db.AiDiagnosticReports.AsNoTracking().OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
            ViewBag.LatestReport = latestReport;
        }

        var chatHistory = await _db.AiChatMessages.AsNoTracking()
            .Where(m => m.UserId == _user.UserId)
            .OrderByDescending(m => m.SentAt)
            .Take(20)
            .ToListAsync();
        chatHistory.Reverse();

        ViewBag.Role = role;
        ViewBag.Query = q ?? "";

        if (partial) return PartialView("_ChatBubble", chatHistory);
        return View(chatHistory);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > _opts.MaxMessageLength)
            return Json(new { success = false, message = "Invalid message (max 500 chars)." });

        // Basic check for API key availability
        bool hasApiKey = !string.IsNullOrWhiteSpace(_opts.GeminiApiKey) || !string.IsNullOrWhiteSpace(_opts.GroqApiKey);
        if (!hasApiKey)
            return Json(new { success = false, message = "AI API keys not configured. Please add GEMINI_API_KEY or GROQ_API_KEY in Render." });

        string role = User.IsInRole(RoleNames.Admin) ? "Admin"
                    : User.IsInRole(RoleNames.Hod) ? "HOD"
                    : User.IsInRole(RoleNames.Faculty) ? "Faculty"
                    : User.IsInRole(RoleNames.Student) ? "Student" : "User";

        // Detect navigation intents (e.g. "go to reports", "go to students", "show report")
        string msgLower = request.Message.Trim().ToLowerInvariant();
        string? navUrl = null;
        if (msgLower.Contains("report") || msgLower.Contains("analytics"))
        {
            navUrl = role == "Student" ? "/Reports/Index" : "/Reports/Index";
        }
        else if (msgLower.Contains("student"))
        {
            navUrl = role is "Admin" or "HOD" or "Faculty" ? "/Students/Index" : "/Student/Index";
        }
        else if (msgLower.Contains("dashboard") || msgLower.Contains("home"))
        {
            navUrl = role == "Student" ? "/Student/Index" : "/Dashboard/Index";
        }
        else if (msgLower.Contains("timetable"))
        {
            navUrl = "/Timetable/Index";
        }
        else if (msgLower.Contains("notice"))
        {
            navUrl = "/Notices/Board";
        }

        string systemPrompt = role switch
        {
            "Student" => """
                You are a friendly tutor for a student in an Indian college (computer science engineering).
                Answer their study questions clearly, step-by-step. Use simple English with occasional Hinglish examples if helpful.
                Keep replies short (under 150 words) and focus on exam preparation.
                If the student asks to navigate or view a page, give them the numeric classroom id in your helpful answer but no link.
                """,
            "Faculty" => """
                You are a teaching assistant for a college faculty member.
                Provide class attendance insights, lesson planning help, and revision notes.
                Be concise and practical for college teaching (under 150 words).
                """,
            _ => """
                You are a System Administrator assistant for AttendX college attendance system.
                Help with system reports, attendance analytics, database diagnostics, and management queries.
                Respond concisely (under 150 words).
                """
        };

        string? reply = await _ai.SendAsync(systemPrompt, request.Message);
        if (string.IsNullOrWhiteSpace(reply))
            return Json(new { success = false, message = "AI is temporarily unavailable. Please try again in a few minutes." });

        var chatEntry = new AiChatMessage
        {
            UserId = _user.UserId ?? "",
            Role = role,
            MessageText = request.Message,
            SentAt = DateTime.Now
        };
        _db.AiChatMessages.Add(chatEntry);
        await _db.SaveChangesAsync();

        // Append a URL marker if navigation was detected - the JS agent parses "GO_TO:" tag
        // We do this server-side so the LLM answer still contains its own text
        if (!string.IsNullOrEmpty(navUrl))
        {
            reply += $"\nGO_TO:{navUrl}";
        }

        return Json(new { success = true, reply });
    }
}
