using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PaperFlow;

public sealed class Storage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "papers.json");
    public string BackupPath => Path.Combine(DirectoryPath, "papers.previous.json");
    public string? RecoveryNotice { get; private set; }
    public string? BackupNotice { get; private set; }
    public Storage(string directory) { DirectoryPath = directory; }

    // Renaming the product renamed the local runtime root. Copy the previous root once,
    // without touching or deleting it, so an upgrade keeps papers, journal, device
    // identity and backups. An explicitly supplied data directory is never migrated.
    private static readonly string[] MigratedEntries = { "papers.json", "papers.previous.json", "journal-initialized.txt", "device-id.txt", "journal", "backups" };
    public static string? MigrateLegacyRoot(string legacy, string current)
    {
        try
        {
            if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)) return null;
            if (File.Exists(Path.Combine(current, "papers.json"))) return null;
            if (!File.Exists(Path.Combine(legacy, "papers.json"))) return null;
            Directory.CreateDirectory(current);
            foreach (var entry in MigratedEntries)
            {
                var source = Path.Combine(legacy, entry);
                var target = Path.Combine(current, entry);
                if (Directory.Exists(source)) Copytree(source, target);
                else if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target, false);
            }
            return File.Exists(Path.Combine(current, "papers.json")) ? Lang.T("已把原 PaperProgress 的本机资料迁移到新目录，原目录保持不动。") : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed migration must not block startup; the shared journal can rebuild.
            return null;
        }
    }
    private static void Copytree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) { var copy = Path.Combine(target, Path.GetFileName(file)); if (!File.Exists(copy)) File.Copy(file, copy, false); }
        foreach (var folder in Directory.GetDirectories(source)) Copytree(folder, Path.Combine(target, Path.GetFileName(folder)));
    }

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
            if (!File.Exists(BackupPath)) throw new InvalidDataException(Lang.F("资料无法读取。原文件已保留在 {0}，请从备份恢复。", damaged), ex);
            var recovered = Parse(File.ReadAllText(BackupPath, Encoding.UTF8));
            File.Copy(BackupPath, FilePath, true);
            RecoveryNotice = Lang.F("已从上次备份恢复。损坏原文件保留在：{0}", damaged);
            return recovered;
        }
    }

    public static Library Parse(string text)
    {
        if (text.Length > 30_000_000) throw new InvalidDataException(Lang.T("导入文件超过 30 MB。"));
        var library = JsonSerializer.Deserialize<Library>(text, JsonOptions) ?? throw new InvalidDataException(Lang.T("资料为空。"));
        Validate(library);
        return library;
    }

    public static void Validate(Library library)
    {
        if (library.Version != 1 || library.Papers == null || library.Settings == null) throw new InvalidDataException(Lang.T("文件版本或结构不受支持。"));
        if (library.Papers.Count > 10000) throw new InvalidDataException(Lang.T("论文数量超过 10000 篇。"));
        if (library.Papers.Any(p => p == null)) throw new InvalidDataException(Lang.T("论文记录不能为 null。"));
        if (library.Papers.Select(p => p.Id).Distinct().Count() != library.Papers.Count) throw new InvalidDataException(Lang.T("论文编号重复。"));
        foreach (var p in library.Papers)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Title) || p.Title.Length > 500)
                throw new InvalidDataException(Lang.T("论文编号或标题无效（标题最多 500 字）。"));
            if (p.Stages == null || p.Stages.Any(s => s == null)) throw new InvalidDataException(Lang.T("论文的阶段数据缺失。"));
            if (p.Stages.Any(s => s.Done && s.Skipped)) throw new InvalidDataException(Lang.T("完成与不适用不能同时选中。"));
            p.SchemeName = string.IsNullOrWhiteSpace(p.SchemeName) ? Schemes.DefaultName : p.SchemeName.Trim();
            var stageProblem = Schemes.Inspect(p.SchemeName, p.Stages.Select(s => s.Name).ToList());
            if (stageProblem != SchemeProblem.None) throw new InvalidDataException(Why(stageProblem));
            p.Tags = (p.Tags ?? new()).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
            if (p.Tags.Count > Schemes.MaxTags) throw new InvalidDataException(Lang.T("一篇论文最多贴三个标签。"));
            if (p.Tags.Any(t => !Schemes.IsValidTagName(t))) throw new InvalidDataException(Lang.T("名字不能超过六个汉字那么宽。"));
            if (p.StartDate.Year < 1900 || p.StartDate.Year > 2200 || p.DueDate?.Year < 1900 || p.DueDate?.Year > 2200)
                throw new InvalidDataException(Lang.T("日期需在 1900—2200 年之间。"));
            if (p.History == null || p.History.Any(h => h == null || h.Description == null)) throw new InvalidDataException(Lang.T("修改记录无效。"));
            p.Subject ??= ""; p.Language ??= ""; p.Collaborators ??= ""; p.Journal ??= "";
            p.NextAction ??= ""; p.Outcome ??= ""; p.Notes ??= "";
            if (!Paper.Priorities.Contains(p.Priority)) throw new InvalidDataException(Lang.T("优先级必须是高、中或低。"));
            if (!Paper.Statuses.Contains(p.Status)) p.Status = "准备中";
        }
        var s = library.Settings;
        // HiddenStages / HideSelectedStages：字段留着给 1.13.x 兼容，2.0.0 的界面会在下一刀移除读取。
        s.HiddenStages = (s.HiddenStages ?? new() { 4 }).Where(i => i >= 0 && i < 7).Distinct().ToList();
        s.CustomSchemes = NormalizeSchemes(s.CustomSchemes);
        s.CustomTags = (s.CustomTags ?? new()).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        if (s.CustomTags.Count > Schemes.MaxCustomTags) throw new InvalidDataException(Lang.T("自建标签最多二十个。"));
        if (s.CustomTags.Any(t => !Schemes.IsValidTagName(t))) throw new InvalidDataException(Lang.T("名字不能超过六个汉字那么宽。"));
        // 认得的标签 = 自建的 + 论文上贴过的（换台电脑时自建标签可能还没跟过来，贴过的一样算数）。
        // 对不上的隐藏勾直接去掉，不留指不到东西的幽灵设置。
        var knownTags = s.CustomTags.Concat(library.Papers.SelectMany(p => p.Tags)).Distinct(StringComparer.Ordinal).ToList();
        s.HiddenTags = (s.HiddenTags ?? new()).Where(knownTags.Contains).Distinct(StringComparer.Ordinal).ToList();
        s.VisiblePriorities = (s.VisiblePriorities ?? Paper.Priorities.ToList()).Where(Paper.Priorities.Contains).Distinct().ToList();
        if (!ViewRules.PageModes.Contains(s.PageMode)) s.PageMode = "不翻页";
        if (!ViewRules.SortModes.Contains(s.SortMode)) s.SortMode = ViewRules.SortModes[0];
        s.PageIndex = Math.Clamp(s.PageIndex, 0, ViewRules.PageCount(s) - 1);
        s.Theme = Themes.IsKnown(s.Theme ?? "") ? Themes.Migrate(s.Theme!) : Themes.Default;
        if (!Themes.Layouts.Contains(s.ListLayout)) s.ListLayout = Themes.CardLayout;
        s.AccentColor ??= ""; s.BackgroundColor ??= ""; s.FontName ??= "Microsoft YaHei UI";
        s.TitleFont = string.IsNullOrWhiteSpace(s.TitleFont) ? "" : s.TitleFont.Trim();
        s.BodyFont = string.IsNullOrWhiteSpace(s.BodyFont) ? "" : s.BodyFont.Trim();
        s.CaptionFont = string.IsNullOrWhiteSpace(s.CaptionFont) ? "" : s.CaptionFont.Trim();
        s.TitleColor = Themes.IsHex(s.TitleColor) ? s.TitleColor : "";
        s.BodyColor = Themes.IsHex(s.BodyColor) ? s.BodyColor : "";
        s.CaptionColor = Themes.IsHex(s.CaptionColor) ? s.CaptionColor : "";
        s.TitleScale = double.IsFinite(s.TitleScale) ? Math.Clamp(s.TitleScale, 0.6, 2) : 1;
        if (!ViewRules.SoundModes.Contains(s.SoundMode)) s.SoundMode = ViewRules.SoundModes[0];
        if (!ViewRules.SoundStyles.Contains(s.SoundStyle)) s.SoundStyle = ViewRules.SoundStyles[0];
        s.SoundVolume = double.IsFinite(s.SoundVolume) ? Math.Clamp(s.SoundVolume, 0, 1) : 0.6;
        s.BodyScale = double.IsFinite(s.BodyScale) ? Math.Clamp(s.BodyScale, 0.6, 2) : 1;
        s.CaptionScale = double.IsFinite(s.CaptionScale) ? Math.Clamp(s.CaptionScale, 0.6, 2) : 1;
        s.SyncFolder ??= ""; s.LauncherPath ??= "";
        // 新字段用 ASCII 码值存储，显示文字随语言走；不认识的值一律回默认。
        s.Language = Lang.Normalize(s.Language);
        if (s.UpdateMode is not ("always" or "daily" or "never")) s.UpdateMode = "daily";
        s.LastUpdateError ??= "";
        s.PendingReleaseVersion ??= "";
        s.PendingReleaseNotes ??= "";
        // 更新说明只用来显示一次；长度不对劲就当没有，不让它留在文件里。
        if (s.PendingReleaseNotes.Length > UpdateManifest.MaxNotesLength) { s.PendingReleaseVersion = ""; s.PendingReleaseNotes = ""; }
        s.BackgroundOpacity = double.IsFinite(s.BackgroundOpacity) ? Math.Clamp(s.BackgroundOpacity, 0.05, 1) : 1;
        s.TextSize = double.IsFinite(s.TextSize) ? Math.Clamp(s.TextSize, 9, 36) : 13;
        s.UiScale = double.IsFinite(s.UiScale) ? Math.Clamp(s.UiScale, 0.8, 2) : 1;
        s.ImageScrim = double.IsFinite(s.ImageScrim) ? Math.Clamp(s.ImageScrim, 0, 0.95) : 0.35;
        // 0 means “follow the theme”; the rest are explicit overrides.
        s.BarHeight = new[] { 0, 6, 14, 20, 28 }.Contains(s.BarHeight) ? s.BarHeight : 0;
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
        { BackupNotice = Lang.T("每日备份失败：") + ex.Message; }
    }

    // 自建方案：结构坏了要响亮报错，不静默修；同名重复的留第一份。
    private static List<StageScheme> NormalizeSchemes(List<StageScheme>? schemes)
    {
        var result = new List<StageScheme>();
        foreach (var scheme in schemes ?? new())
        {
            if (scheme == null) throw new InvalidDataException(Lang.T("方案数据为空。"));
            string name = (scheme.Name ?? "").Trim();
            var stages = (scheme.StageNames ?? new()).Select(s => (s ?? "").Trim()).ToList();
            var problem = Schemes.Inspect(name, stages);
            if (problem != SchemeProblem.None) throw new InvalidDataException(Why(problem));
            if (Schemes.IsBuiltIn(name)) throw new InvalidDataException(Lang.T("自建方案不能和内置方案同名。"));
            if (result.Any(x => x.Name == name)) continue;
            result.Add(new StageScheme(name, stages));
        }
        return result;
    }

    // 一套方案为什么不能用。界面提示也复用这几句，避免两处说法不一致。
    public static string Why(SchemeProblem problem) => problem switch
    {
        SchemeProblem.TooFewStages or SchemeProblem.TooManyStages => Lang.T("一套方案至少 2 个阶段、最多 12 个。"),
        SchemeProblem.EmptyName => Lang.T("名字不能为空。"),
        SchemeProblem.DuplicateName => Lang.T("同一套方案里不能有同名的阶段。"),
        _ => Lang.T("名字不能超过十二个汉字那么宽。")
    };

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
