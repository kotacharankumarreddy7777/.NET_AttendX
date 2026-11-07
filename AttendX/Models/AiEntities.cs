namespace AttendX.Models;

/// <summary>AI auto-diagnostic report generated in background (Admin only).</summary>
public class AiDiagnosticReport
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    [StringLength(4000)]
    public string ReportText { get; set; } = "";
    public int HealthScore { get; set; }
}

public class AiChatMessage
{
    public int Id { get; set; }
    [StringLength(450)]
    public string UserId { get; set; } = "";
    [StringLength(50)]
    public string Role { get; set; } = "";
    public string MessageText { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.Now;
}

public class AiOptions
{
    public string GeminiApiKey { get; set; } = "";
    public string GroqApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-1.5-flash";
    public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
    public int DiagnosticsIntervalMinutes { get; set; } = 5;
    public int ChatRateLimitPerMinute { get; set; } = 10;
    public int MaxMessageLength { get; set; } = 500;
}
