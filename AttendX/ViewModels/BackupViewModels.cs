namespace AttendX.ViewModels;

public class BackupFileInfo
{
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BackupViewModel
{
    public List<BackupFileInfo> Files { get; set; } = new();
    public List<BackupHistory> History { get; set; } = new();
    public string Folder { get; set; } = "";
    public string DatabaseName { get; set; } = "";
}
