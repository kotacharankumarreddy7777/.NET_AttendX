using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QRCoder;

namespace AttendX.Services;

/// <summary>
/// Signing logic (pure, unit-testable). Student QR payload:  AX1.{studentDbId}.{studentCode}.{signature}
/// The signature is an HMAC-SHA256 with a server secret, so a QR cannot be forged or edited.
/// Session tokens rotate every QrExpirySeconds; an expired / previous-window token is rejected (replay protection).
/// </summary>
public class QrSigner(string secret)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(secret);

    private string Sign(string data) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(data)))[..20];

    private static bool SafeEquals(string a, string b)
        => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public string BuildStudentPayload(int studentId, string studentCode)
        => $"AX1.{studentId}.{studentCode}.{Sign($"STU|{studentId}|{studentCode}")}";

    public bool TryParseStudentPayload(string? payload, out int studentId, out string studentCode)
    {
        studentId = 0; studentCode = "";
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 200) return false;
        var parts = payload.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != "AX1") return false;
        if (!int.TryParse(parts[1], out var id)) return false;
        if (!SafeEquals(parts[3], Sign($"STU|{id}|{parts[2]}"))) return false;
        studentId = id; studentCode = parts[2];
        return true;
    }

    private static long Window(DateTimeOffset now, int expirySeconds) => now.ToUnixTimeSeconds() / Math.Max(1, expirySeconds);

    public string CreateSessionToken(int sessionId, DateTimeOffset now, int expirySeconds)
        => Sign($"SES|{sessionId}|{Window(now, expirySeconds)}");

    /// <summary>Valid for the current window and the one before it (small allowance for network delay).</summary>
    public bool ValidateSessionToken(int sessionId, string? token, DateTimeOffset now, int expirySeconds)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var w = Window(now, expirySeconds);
        return SafeEquals(token, Sign($"SES|{sessionId}|{w}")) || SafeEquals(token, Sign($"SES|{sessionId}|{w - 1}"));
    }
}

public interface IQrService
{
    QrSigner Signer { get; }
    byte[] RenderPng(string payload, int pixelsPerModule = 10);
    Task<string> GenerateStudentQrAsync(Student student);
    string GetStudentPayload(Student student);
}

public class QrService(IOptions<AttendXOptions> options, IWebHostEnvironment env) : IQrService
{
    private readonly IWebHostEnvironment _env = env;
    public QrSigner Signer { get; } = new QrSigner(options.Value.QrSecret);

    public string GetStudentPayload(Student s) => Signer.BuildStudentPayload(s.Id, s.StudentID);

    public byte[] RenderPng(string payload, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    /// <summary>Creates (or replaces) the student's QR image under wwwroot/QRCodes and returns the relative path.</summary>
    public Task<string> GenerateStudentQrAsync(Student student)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(webRoot, "QRCodes");
        Directory.CreateDirectory(dir);

        // delete the previous image (only by file name, so a tampered DB path can never escape the folder)
        if (!string.IsNullOrEmpty(student.QRPath))
        {
            var old = Path.Combine(dir, Path.GetFileName(student.QRPath));
            if (File.Exists(old)) File.Delete(old);
        }

        var fileName = Guid.NewGuid().ToString("N") + ".png";
        File.WriteAllBytes(Path.Combine(dir, fileName), RenderPng(GetStudentPayload(student)));
        return Task.FromResult("QRCodes/" + fileName);
    }
}
