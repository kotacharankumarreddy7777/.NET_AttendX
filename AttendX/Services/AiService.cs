using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface IAiService
{
    Task<string?> SendAsync(string systemPrompt, string userPrompt);
    Task<(string Text, int Score)> EvaluateHealthAsync(string metricsSummary);
}

public class AiService(HttpClient http, IOptions<AiOptions> opts, ILogger<AiService> log) : IAiService
{
    private readonly HttpClient _http = http;
    private readonly AiOptions _opts = opts.Value;
    private readonly ILogger<AiService> _log = log;

    public async Task<string?> SendAsync(string systemPrompt, string userPrompt)
    {
        // Try Gemini first
        if (!string.IsNullOrWhiteSpace(_opts.GeminiApiKey))
        {
            var geminiResult = await TryGeminiAsync(systemPrompt, userPrompt);
            if (!string.IsNullOrWhiteSpace(geminiResult)) return geminiResult;
        }
        // Fallback to Groq
        if (!string.IsNullOrWhiteSpace(_opts.GroqApiKey))
        {
            return await TryGroqAsync(systemPrompt, userPrompt);
        }
        // Return null so controller can display user-friendly error
        return null;
    }

    public async Task<(string Text, int Score)> EvaluateHealthAsync(string metricsSummary)
    {
        string systemPrompt = """
            You are an expert system health AI evaluator for a college attendance management system.
            Review the metrics provided, assign a Health Score from 0-100 (100=perfect), and provide a concise 3-4 bullet report of issues and recommendations.
            Format your response EXACTLY as: SCORE: [0-100]
            REPORT: [full report text]
            """;
        var response = await SendAsync(systemPrompt, metricsSummary);
        if (string.IsNullOrWhiteSpace(response)) 
        {
            // If API is unavailable or not configured, report healthy-but-pending instead of failed
            bool hasKeys = !string.IsNullOrWhiteSpace(_opts.GeminiApiKey) || !string.IsNullOrWhiteSpace(_opts.GroqApiKey);
            if (!hasKeys) return ("AI API keys not configured. System health cannot be evaluated. Using heuristic fallback.", 85);
            return ("AI evaluation temporarily unavailable, system metrics nominal.", 80);
        }

        int score = 90;
        string report = response;
        try
        {
            if (response.Contains("SCORE:"))
            {
                var parts = response.Split("SCORE:");
                if (parts.Length > 1)
                {
                    var scorePart = parts[1].Split("REPORT:")[0].Trim();
                    if (int.TryParse(scorePart, out int parsedScore)) score = parsedScore;
                    report = response.Substring(response.IndexOf("REPORT:"))["REPORT:".Length..].Trim();
                }
            }
        }
        catch { }
        if (string.IsNullOrWhiteSpace(report)) report = response;
        return (report, Math.Clamp(score, 0, 100));
    }

    private async Task<string?> TryGeminiAsync(string systemPrompt, string userPrompt)
    {
        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new {
                        role = "user",
                        parts = new[]
                        {
                            new { text = $"{systemPrompt}\n\n{userPrompt}" }
                        }
                    }
                },
                generationConfig = new { maxOutputTokens = 200, temperature = 0.8 }
            };
            var json = JsonSerializer.Serialize(payload);
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opts.GeminiModel}:generateContent";
            var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?key={_opts.GeminiApiKey}");
            request.Content = stringContent;

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _log.LogWarning("Gemini request failed with {StatusCode}: {Body}", response.StatusCode, body.Substring(0, Math.Min(body.Length, 500)));
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                return null;
            if (!candidates[0].TryGetProperty("content", out var content)) return null;
            if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0) return null;
            if (!parts[0].TryGetProperty("text", out var textElem)) return null;
            return textElem.GetString()?.Trim();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Gemini API call failed");
            return null;
        }
    }

    private async Task<string?> TryGroqAsync(string systemPrompt, string userPrompt)
    {
        try
        {
            var payload = new
            {
                model = _opts.GroqModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = 250,
                temperature = 0.8
            };
            var json = JsonSerializer.Serialize(payload);
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.GroqApiKey);
            request.Content = stringContent;

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _log.LogWarning("Groq request failed with {StatusCode}: {Body}", response.StatusCode, body.Substring(0, Math.Min(body.Length, 500)));
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
            if (!choices[0].TryGetProperty("message", out var msg)) return null;
            if (!msg.TryGetProperty("content", out var content)) return null;
            return content.GetString()?.Trim();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Groq API call failed");
            return null;
        }
    }
}
