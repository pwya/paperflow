using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperFlow;

public sealed class SyncEdit
{
    public string PaperId { get; set; } = "";
    public string Field { get; set; } = "";
    public JsonElement Value { get; set; }
    // 本机不认识这个字段（多半来自更新版本）。跳过它，但保留文件，升级后再补上。
    [JsonIgnore] public bool Unknown { get; set; }
}
public sealed class SyncEvent
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Device { get; set; } = "";
    public long Counter { get; set; }
    public List<SyncEdit> Edits { get; set; } = new();
    [JsonIgnore] public int Unknown { get; set; }
    // 整条记录来自更新版本：文件保留，等升级后再读，绝不丢弃。
    [JsonIgnore] public bool Unsupported { get; set; }
}

// Each edit is an immutable, uniquely named file. The synced folder transports files; it never
// has to merge two devices writing the same papers.json. Scalar fields and each stage
// are independent last-writer registers ordered by Lamport counter, device id, event id.
public static class SyncProtocol
{
    // Tags / SchemeName 走标量：老版本不认识它们，会按既有桥梁"跳过并保留"，不会整条拒绝。
    private static readonly string[] ScalarNames = { "Title", "Subject", "Language", "Collaborators", "Journal", "Status", "Priority", "NextAction", "Outcome", "Notes", "StartDate", "DueDate", "UpdatedAt", "Archived", "SchemeName", "Tags" };
    private static readonly Dictionary<string, PropertyInfo> Scalars = ScalarNames.ToDictionary(n => n, n => typeof(Paper).GetProperty(n)!);
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);
    public static List<SyncEdit> Diff(Library before, Library after)
    {
        Storage.Validate(after);
        var edits = new List<SyncEdit>();
        var old = before.Papers.ToDictionary(p => p.Id);
        foreach (var p in after.Papers)
        {
            old.TryGetValue(p.Id, out var previous);
            void Add(string field, object? value) => edits.Add(new SyncEdit { PaperId = p.Id, Field = field, Value = Json(value) });
            foreach (var field in Scalars)
                if (previous == null || Json(field.Value.GetValue(previous)).GetRawText() != Json(field.Value.GetValue(p)).GetRawText()) Add(field.Key, field.Value.GetValue(p));
            for (int i = 0; i < 7; i++) if (previous == null || p.Stages[i].Done != previous.Stages[i].Done || p.Stages[i].Skipped != previous.Stages[i].Skipped) Add("stage:" + i, p.Stages[i]);
            var history = p.History.Where(h => previous == null || !previous.History.Any(x => x.At == h.At && x.Description == h.Description)).ToList();
            if (history.Count > 0) Add("history", history);
            if (previous == null || before.Papers.FindIndex(x => x.Id == p.Id) != after.Papers.IndexOf(p)) Add("position", after.Papers.IndexOf(p));
        }
        return edits;
    }
    public static SyncEvent Parse(string text)
    {
        if (text.Length > 30_000_000) throw new InvalidDataException(Lang.T("同步记录过大。"));
        var ev = JsonSerializer.Deserialize<SyncEvent>(text) ?? throw new InvalidDataException(Lang.T("同步记录为空。"));
        // 读别人的记录用宽松检查；本机自己写出去的记录在 Commit 里用严格检查。
        Inspect(ev); return ev;
    }
    public static void Validate(SyncEvent ev)
    {
        Inspect(ev);
        // 本机自己生成的记录必须完全看得懂；看不懂说明是程序 bug，要响。
        if (ev.Unsupported) throw new InvalidDataException(Lang.T("同步记录版本不受支持：") + ev.Version);
        if (ev.Unknown > 0) throw new InvalidDataException(Lang.T("本机生成的同步记录含未知字段。"));
    }
    // 兼容性约定：结构坏了照旧抛错；只是"本机不认识"的内容跳过并计数，
    // 让同一条记录里其他能读懂的改动照样生效。文件始终保留，升级后自动补上。
    public static void Inspect(SyncEvent ev)
    {
        if (!Guid.TryParseExact(ev.Id, "N", out _) || !Guid.TryParseExact(ev.Device, "N", out _) || ev.Counter < 1 || ev.Counter > long.MaxValue - 1000 || ev.Edits == null || ev.Edits.Count == 0 || ev.Edits.Count > 300000)
            throw new InvalidDataException(Lang.T("同步记录格式不受支持。"));
        ev.Unknown = 0; ev.Unsupported = ev.Version != 1;
        if (ev.Unsupported) return;
        if (ev.Edits.Any(x => x == null)) throw new InvalidDataException(Lang.T("空修改记录。"));
        foreach (var edit in ev.Edits)
        {
            edit.Unknown = false;
            if (string.IsNullOrWhiteSpace(edit.Field)) throw new InvalidDataException(Lang.T("同步字段缺失。"));
            if (string.IsNullOrWhiteSpace(edit.PaperId) || edit.PaperId.Length > 200) throw new InvalidDataException(Lang.T("论文编号无效。"));
            if (Scalars.TryGetValue(edit.Field, out var property))
            {
                var value = edit.Value.Deserialize(property.PropertyType);
                if (property.PropertyType == typeof(string) && value is not string) throw new InvalidDataException(Lang.T("文字字段不能为空值。"));
                if (edit.Field == "Title" && (value is not string title || string.IsNullOrWhiteSpace(title) || title.Length > 500)) throw new InvalidDataException(Lang.T("论文标题无效。"));
                if (edit.Field == "Priority" && (value is not string priority || !Paper.Priorities.Contains(priority))) throw new InvalidDataException(Lang.T("优先级无效。"));
                if ((edit.Field == "StartDate" || edit.Field == "DueDate") && value is DateTime date && (date.Year < 1900 || date.Year > 2200)) throw new InvalidDataException(Lang.T("日期无效。"));
            }
            else if (edit.Field.StartsWith("stage:", StringComparison.Ordinal))
            {
                if (!int.TryParse(edit.Field[6..], out int i) || i < 0 || i > 6) { edit.Unknown = true; ev.Unknown++; continue; }
                var stage = edit.Value.Deserialize<Stage>();
                if (stage == null || stage.Done && stage.Skipped || stage.Skipped && i != 5) throw new InvalidDataException(Lang.T("阶段无效。"));
                if (stage.Name != Paper.StageNames[i]) { edit.Unknown = true; ev.Unknown++; }
            }
            else if (edit.Field == "position") { if (!edit.Value.TryGetInt32(out int position) || position < 0) throw new InvalidDataException(Lang.T("排序无效。")); }
            else if (edit.Field == "history")
            {
                var history = edit.Value.Deserialize<List<Change>>();
                if (history == null || history.Any(h => h == null || h.Description == null)) throw new InvalidDataException(Lang.T("历史记录无效。"));
            }
            else { edit.Unknown = true; ev.Unknown++; }
        }
    }
    public static Library Reduce(IEnumerable<SyncEvent> source)
    {
        var papers = new Dictionary<string, Paper>(); var positions = new Dictionary<string, int>();
        foreach (var ev in source.Where(e => !e.Unsupported).GroupBy(e => e.Id).Select(g => g.First()).OrderBy(e => e.Counter).ThenBy(e => e.Device, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            Inspect(ev);
            foreach (var edit in ev.Edits)
            {
                if (edit.Unknown) continue;
                if (!papers.TryGetValue(edit.PaperId, out var p)) { p = new Paper { Id = edit.PaperId }; papers.Add(p.Id, p); }
                if (Scalars.TryGetValue(edit.Field, out var property)) property.SetValue(p, edit.Value.Deserialize(property.PropertyType));
                else if (edit.Field.StartsWith("stage:", StringComparison.Ordinal)) p.Stages[int.Parse(edit.Field[6..])] = edit.Value.Deserialize<Stage>()!;
                else if (edit.Field == "position") positions[p.Id] = edit.Value.GetInt32();
                else foreach (var h in edit.Value.Deserialize<List<Change>>()!) if (!p.History.Any(x => x.At == h.At && x.Description == h.Description)) p.History.Add(h);
            }
        }
        // A patch can arrive before its creation file. Keep it in the journal and display
        // the paper once its required title arrives; never invent missing user content.
        var result = new Library { Papers = papers.Values.Where(p => !string.IsNullOrWhiteSpace(p.Title)).OrderBy(p => positions.GetValueOrDefault(p.Id, int.MaxValue)).ThenBy(p => p.Id, StringComparer.Ordinal).ToList() };
        foreach (var p in result.Papers) p.History = p.History.OrderByDescending(h => h.At).ThenBy(h => h.Description, StringComparer.Ordinal).ToList();
        Storage.Validate(result); return result;
    }
    public static void ApplyEdits(Library target, IEnumerable<SyncEdit> edits)
    {
        // Used by an editor opened before a remote update: apply only fields the user
        // changed, preserving unrelated fields that arrived while the dialog was open.
        foreach (var edit in edits)
        {
            if (edit.Unknown) continue;
            var p = target.Papers.Single(x => x.Id == edit.PaperId);
            if (Scalars.TryGetValue(edit.Field, out var property)) property.SetValue(p, edit.Value.Deserialize(property.PropertyType));
            else if (edit.Field.StartsWith("stage:", StringComparison.Ordinal)) p.Stages[int.Parse(edit.Field[6..])] = edit.Value.Deserialize<Stage>()!;
            else if (edit.Field == "history") foreach (var h in edit.Value.Deserialize<List<Change>>()!) if (!p.History.Any(x => x.At == h.At && x.Description == h.Description)) p.History.Add(h);
        }
    }
}

public sealed class SyncEngine
{
    private readonly object gate = new();
    private readonly Dictionary<string, SyncEvent> events = new();
    private readonly string localJournal;
    private readonly string device;
    // 留空表示“还没说过话”，届时按当前语言取默认文案——语言可能比同步引擎晚一步准备好。
    private string status = "";
    public string Folder { get; }
    public string Status { get { lock (gate) return status.Length == 0 ? Lang.T("本机自动保存") : status; } }
    public int Revision { get { lock (gate) return events.Count; } }
    // 有几条记录带着本机不认识的内容（多半来自更新版本）。升级后会自动补上。
    public int Held { get { lock (gate) return events.Values.Count(e => e.Unsupported || e.Unknown > 0); } }
    public SyncEngine(string localDirectory, string sharedDirectory, Library legacy)
    {
        Folder = sharedDirectory;
        // This personal library keeps a local journal. No private absolute paths go
        // into shared event files. Device ids are random, not host names or usernames.
        var libraryKey = "journal";
        localJournal = Path.Combine(localDirectory, libraryKey); Directory.CreateDirectory(localJournal);
        var identity = Path.Combine(localDirectory, "device-id.txt");
        if (!File.Exists(identity)) AtomicWrite(identity, Guid.NewGuid().ToString("N"));
        device = File.ReadAllText(identity).Trim();
        if (!Guid.TryParseExact(device, "N", out _)) throw new InvalidDataException(Lang.T("本机同步标识损坏，请保留资料后重新配置。"));
        foreach (var path in Directory.GetFiles(localJournal, "*.json")) { var ev = SyncProtocol.Parse(File.ReadAllText(path)); events.TryAdd(ev.Id, ev); }
        var initialized = Path.Combine(localDirectory, "journal-initialized.txt");
        if (!File.Exists(initialized))
        {
            var current = Snapshot(); var imported = Storage.Merge(current, legacy);
            Commit(current, imported); AtomicWrite(initialized, "1");
        }
    }
    public Library Snapshot() { lock (gate) return SyncProtocol.Reduce(events.Values); }
    public void Commit(Library before, Library after)
    {
        var changes = SyncProtocol.Diff(before, after); if (changes.Count == 0) return;
        lock (gate)
        {
            var ev = new SyncEvent { Device = device, Counter = events.Count == 0 ? 1 : events.Values.Max(x => x.Counter) + 1, Edits = changes };
            SyncProtocol.Validate(ev);
            // Local append is the commit point, before any network filesystem operation.
            AtomicWrite(Path.Combine(localJournal, ev.Id + ".json"), JsonSerializer.Serialize(ev, Storage.JsonOptions));
            events.Add(ev.Id, ev); status = Folder == "" ? Lang.T("本机已保存") : Lang.T("本机已保存 · 等待写入同步文件夹");
        }
    }
    public bool Poll()
    {
        if (string.IsNullOrWhiteSpace(Folder)) return false;
        bool changed = false;
        int unreadable = 0;
        try
        {
            var shared = Path.Combine(Folder, "events-v1"); Directory.CreateDirectory(shared);
            // Cache received immutable events locally. Network IO is outside the gate.
            foreach (var path in Directory.GetFiles(shared, "*.json"))
            {
                try
                {
                var name = Path.GetFileNameWithoutExtension(path);
                lock (gate) { if (events.ContainsKey(name)) continue; }
                if (new FileInfo(path).Length > 30_000_000) throw new InvalidDataException(Lang.T("同步文件过大：") + Path.GetFileName(path));
                var text = File.ReadAllText(path); var ev = SyncProtocol.Parse(text);
                lock (gate)
                {
                    if (events.ContainsKey(ev.Id)) continue;
                    AtomicWrite(Path.Combine(localJournal, ev.Id + ".json"), text);
                    events.Add(ev.Id, ev); changed = true;
                }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException)
                { unreadable++; } // Keep the original file for diagnosis and retry next poll; other events still flow.
            }
            SyncEvent[] copies; lock (gate) copies = events.Values.ToArray();
            foreach (var ev in copies)
            {
                var destination = Path.Combine(shared, ev.Id + ".json");
                if (!File.Exists(destination)) AtomicWrite(destination, JsonSerializer.Serialize(ev, Storage.JsonOptions));
            }
            int held = Held;
            lock (gate) status = unreadable > 0
                ? Lang.F("本机已保存 · {0} 个同步文件暂无法读取，将重试", unreadable)
                : held > 0
                    ? Lang.F("同步文件夹已更新 · {0} 条记录含更新版本的字段，已保留待升级后生效", held)
                    : Lang.T("同步文件夹已更新 · ") + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { lock (gate) status = Lang.F("本机已保存 · 同步待重试（{0}）", ex.Message); }
        return changed;
    }
    public static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) { writer.Write(text); writer.Flush(); stream.Flush(true); }
        File.Move(temp, path, false);
    }
}
