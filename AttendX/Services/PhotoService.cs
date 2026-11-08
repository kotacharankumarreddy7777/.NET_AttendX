using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface IPhotoService
{
    Task<(string? Path, string? Error)> SaveAsync(IFormFile? file, string? webcamDataUrl);
    void DeleteSafe(string? relativePath);
}

/// <summary>Secure photo handling: extension + content-type + size + real file signature (magic bytes) checks, random file names, fixed folder.</summary>
public class PhotoService(IWebHostEnvironment env, IOptions<AttendXOptions> o) : IPhotoService
{
    private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] AllowedTypes = { "image/jpeg", "image/png", "image/webp" };
    private readonly IWebHostEnvironment _env = env;
    private readonly AttendXOptions _o = o.Value;

    private string Dir()
    {
        var d = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads", "students");
        Directory.CreateDirectory(d);
        return d;
    }

    private static string? DetectExt(byte[] b)
    {
        if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        return null;
    }

    public async Task<(string? Path, string? Error)> SaveAsync(IFormFile? file, string? webcamDataUrl)
    {
        long max = _o.MaxUploadMB * 1024L * 1024L;
        byte[]? bytes = null;

        if (file != null && file.Length > 0)
        {
            if (file.FileName == null) return (null, "Photo file name is required.");
            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExt.Contains(ext)) return (null, "Photo must be a .jpg, .jpeg, .png or .webp file.");
            if (!AllowedTypes.Contains((file.ContentType ?? "").ToLowerInvariant())) return (null, "Photo content type is not allowed.");
            if (file.Length > max) return (null, $"Photo is larger than {_o.MaxUploadMB} MB.");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            if (ms.Length > max) return (null, $"Photo is larger than {_o.MaxUploadMB} MB.");
            bytes = ms.ToArray();
        }
        else if (!string.IsNullOrWhiteSpace(webcamDataUrl))
        {
            var idx = webcamDataUrl.IndexOf("base64,", StringComparison.Ordinal);
            if (!webcamDataUrl.StartsWith("data:image/") || idx < 0) return (null, "Webcam photo is invalid.");
            var b64 = webcamDataUrl[(idx + 7)..];
            if (b64.Length > (max * 2)) return (null, $"Photo is larger than {_o.MaxUploadMB} MB.");
            try { bytes = Convert.FromBase64String(b64); }
            catch (FormatException) { return (null, "Webcam photo is invalid."); }
            if (bytes.Length > max) return (null, $"Photo is larger than {_o.MaxUploadMB} MB.");
        }

        if (bytes == null) return (null, null);
        var real = DetectExt(bytes);
        if (real == null) return (null, "The file is not a real JPEG, PNG or WEBP image.");

        var name = Guid.NewGuid().ToString("N") + real;
        var tmp = Path.Combine(Dir(), name + ".tmp");
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                await fs.WriteAsync(bytes);
            }
            File.Move(tmp, Path.Combine(Dir(), name), overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
        return ("uploads/students/" + name, null);
    }

    public void DeleteSafe(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        var p = System.IO.Path.Combine(Dir(), System.IO.Path.GetFileName(relativePath));
        if (File.Exists(p)) File.Delete(p);
    }
}
