using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PaperProgress;

public sealed class Storage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "papers.json");
    public string BackupPath => Path.Combine(DirectoryPath, "papers.previous.json");
    public string? RecoveryNotice { get; private set; }
    public string? BackupNotice { get; private set; }
    public Storage(string directory) { DirectoryPath = directory; }

    public Library Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(FilePath)) return new Library();
        try { return Parse(File.ReadAllText(FilePath, Encoding.UTF8)); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            // Preserve the damaged original before restoring the last verified snapshot.
            var damaged = Path.Combine(DirectoryPath, $"papers.damaged-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Copy(FilePath, damaged, false);
            if (!File.Exists(BackupPath)) throw new InvalidDataException($"资料无法读取。原文件已保留在 {damaged}，请从备份恢复。", ex);
            var recovered = Parse(File.ReadAllText(BackupPath, Encoding.UTF8));
            File.Copy(BackupPath, FilePath, true);
            RecoveryNotice = $"已从上次备份恢复。损坏原文件保留在：{damaged}";
            return recovered;
        }
    }

    public static Library Parse(string text)
    {
        if (text.Length > 30_000_000) throw new InvalidDataException("导入文件超过 30 MB。");
        var library = JsonSerializer.Deserialize<Library>(text, JsonOptions) ?? throw new InvalidDataException("资料为空。");
        Validate(library);
        return library;
    }

    public static void Validate(Library library)
    {
        if (library.Version != 1 || library.Papers == null || library.Settings == null) throw new InvalidDataException("文件版本或结构不受支持。");
        if (library.Papers.Count > 10000) throw new InvalidDataException("论文数量超过 10000 篇。");
        if (library.Papers.Any(p => p == null)) throw new InvalidDataException("论文记录不能为 null。");
        if (library.Papers.Select(p => p.Id).Distinct().Count() != library.Papers.Count) throw new InvalidDataException("论文编号重复。");
        foreach (var p in library.Papers)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Title) || p.Title.Length > 500)
                throw new InvalidDataException("论文编号或标题无效（标题最多 500 字）。");
            if (p.Stages == null || p.Stages.Count != 7 || p.Stages.Where((s, i) => s == null || s.Name != Paper.StageNames[i] || (s.Done && s.Skipped)).Any())
                throw new InvalidDataException("必须包含完整的七个标准阶段，且完成与不适用不能同时选中。");
            if (p.Stages.Where((s, i) => s.Skipped && i != 5).Any()) throw new InvalidDataException("仅返修阶段允许设为不适用。");
            if (p.StartDate.Year < 1900 || p.StartDate.Year > 2200 || p.DueDate?.Year < 1900 || p.DueDate?.Year > 2200)
                throw new InvalidDataException("日期需在 1900—2200 年之间。");
            if (p.History == null || p.History.Any(h => h == null || h.Description == null)) throw new InvalidDataException("修改记录无效。");
            p.Subject ??= ""; p.Language ??= ""; p.Collaborators ??= ""; p.Journal ??= "";
            p.NextAction ??= ""; p.Outcome ??= ""; p.Notes ??= "";
            if (!Paper.Statuses.Contains(p.Status)) p.Status = "准备中";
        }
        var s = library.Settings;
        s.Theme ??= "竹青"; s.AccentColor ??= ""; s.BackgroundColor ??= ""; s.FontName ??= "Microsoft YaHei UI";
        s.SyncFolder ??= ""; s.LauncherPath ??= "";
        s.BackgroundOpacity = double.IsFinite(s.BackgroundOpacity) ? Math.Clamp(s.BackgroundOpacity, 0.05, 1) : 1;
        s.TextSize = double.IsFinite(s.TextSize) ? Math.Clamp(s.TextSize, 10, 22) : 13;
        s.BarHeight = new[] { 14, 20, 28 }.Contains(s.BarHeight) ? s.BarHeight : 20;
        s.Width = double.IsFinite(s.Width) ? Math.Clamp(s.Width, 480, 1800) : 650;
        s.Height = double.IsFinite(s.Height) ? Math.Clamp(s.Height, 400, 1600) : 840;
        s.Left = double.IsFinite(s.Left) ? s.Left : -1; s.Top = double.IsFinite(s.Top) ? s.Top : -1;
    }

    public void Save(Library library)
    {
        Validate(library);
        Directory.CreateDirectory(DirectoryPath);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(library, JsonOptions));
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, BackupPath, true);
        else File.Move(temp, FilePath);
        // Daily snapshots supplement the immediately preceding save. No automatic deletion.
        var daily = Path.Combine(DirectoryPath, "backups", $"papers-{DateTime.Today:yyyy-MM-dd}.json");
        BackupNotice = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(daily)!);
            if (!File.Exists(daily)) File.Copy(FilePath, daily);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { BackupNotice = "每日备份失败：" + ex.Message; }
    }

    public static Paper Clone(Paper p) => JsonSerializer.Deserialize<Paper>(JsonSerializer.Serialize(p))!;
    public static Library CloneLibrary(Library source) => Parse(JsonSerializer.Serialize(source));

    public static Library Merge(Library current, Library incoming)
    {
        Validate(incoming);
        var merged = CloneLibrary(current);
        // Existing IDs are never overwritten by an import. Import is additive and idempotent.
        foreach (var p in incoming.Papers)
            if (!merged.Papers.Any(x => x.Id == p.Id)) merged.Papers.Add(Clone(p));
        Validate(merged);
        return merged;
    }
}
