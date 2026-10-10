using PaperFlow;
using System.Text.Json;

static class ResourceTimeTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "PaperFlow-resource-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "示例 论文文件夹"); Directory.CreateDirectory(folder);
        foreach (string url in new[] { "https://example.com/submission", "http://example.com/a?q=中文", " HTTPS://example.com/a%20b " })
        {
            check(PaperResources.TryResolve(url, out var web) && web?.Kind == "web", "accept HTTP/HTTPS URL " + url);
            check(PaperResources.LaunchTarget(web!) == new Uri(url.Trim()).AbsoluteUri, "launch only the saved HTTP/HTTPS target");
        }
        check(PaperResources.TryResolve(folder, out var local) && local?.Kind == "folder" && local.Target == folder, "accept Chinese and space-containing folder");
        check(PaperResources.LaunchTarget(local!) == folder, "folder launch preserves its entire path");
        var file = Path.Combine(root, "sample.txt"); File.WriteAllText(file, "synthetic");
        foreach (string invalid in new[] { "javascript:alert(1)", "file:///sample", "ftp://example.com", "https://", "example.com", "./relative", file, Path.Combine(root, "missing") })
            check(!PaperResources.TryResolve(invalid, out _), "reject unsupported or invalid quick link " + invalid);
        check(PaperResources.TryResolve("  ", out var cleared) && cleared == null, "empty input removes a link");
        check(PaperResources.LaunchTarget(new() { Kind = "program", Target = file }) == null, "unknown type cannot start a program");
        Directory.Delete(folder);
        check(PaperResources.LaunchTarget(local!) == null && local!.Target == folder, "missing folder fails without clearing configuration");
        var today = new DateTime(2026, 8, 20);
        var paper = new Paper { Title = "Synthetic countdown", StartDate = today.AddDays(-20), DueDate = today.AddDays(3), UpdatedAt = today };
        check(ViewRules.TimeText(paper, "auto", today) == "还剩 3 天", "auto favours countdown");
        check(ViewRules.TimeText(paper, "countdown", today) == "还剩 3 天", "countdown mode");
        check(ViewRules.TimeText(paper, "elapsed", today) == "已开始 20 天", "elapsed mode remains independent");
        check(ViewRules.TimeText(paper, "both", today) == "还剩 3 天 · 已开始 20 天", "both puts countdown first");
        check(ViewRules.TimeText(paper, "hidden", today) == "", "hidden mode");
        check(ViewRules.TimeText(paper, "unknown", today) == "还剩 3 天", "unknown mode defaults to auto");
        check(ViewRules.TimeText(paper, "auto", today.AddDays(2)) == "还剩 1 天", "cross-day recomputation");
        check(ViewRules.TimeText(paper, "auto", today.AddDays(3)) == "今天截止", "due today");
        check(ViewRules.TimeText(paper, "auto", today.AddDays(5)) == "已逾期 2 天", "overdue text");
        paper.DueDate = null;
        check(ViewRules.TimeText(paper, "auto", today) == "已开始 20 天", "missing due date falls back to elapsed");
        check(ViewRules.TimeText(paper, "countdown", today) == "", "countdown only stays empty without a date");
        check(ViewRules.TimeText(paper, "both", today) == "已开始 20 天", "both avoids an empty separator");
        paper.DueDate = today.AddDays(-5); foreach (var stage in paper.Stages) stage.Done = true;
        check(ViewRules.TimeText(paper, "countdown", today) == "" && !ViewRules.ShowsCountdown(paper, "both"), "complete paper has no deadline reminder");
        check(paper.DueDate == today.AddDays(-5), "completion preserves the date");
        paper.Stages[0].Done = false;
        check(ViewRules.TimeText(paper, "auto", today) == "已逾期 5 天", "reopening restores the reminder");
        var legacy = Storage.Parse("{\"Version\":1,\"Papers\":[{\"Title\":\"Synthetic legacy\",\"StartDate\":\"2026-01-02\",\"DueDate\":\"2026-08-20\"}]}");
        check(legacy.Settings.ShowResourceLinks && legacy.Settings.TimeDisplayMode == "auto" && legacy.Settings.PaperResources.Count == 0, "new local settings get backward-compatible defaults");
        check(legacy.Papers[0].StartDate == new DateTime(2026, 1, 2) && legacy.Papers[0].DueDate == today, "legacy dates are not migrated or reset");
        legacy.Settings.TimeDisplayMode = "future-mode"; Storage.Validate(legacy);
        check(legacy.Settings.TimeDisplayMode == "auto", "unknown saved mode falls back");
        var before = new Library { Papers = new() { Storage.Clone(paper) } };
        var edit = Storage.CloneLibrary(before);
        PaperEditorChanges.Apply(edit, before.Papers[0], Storage.Clone(before.Papers[0]), local);
        check(SyncProtocol.Diff(before, edit).Count == 0 && edit.Papers[0].UpdatedAt == today && edit.Papers[0].History.Count == 0, "resource-only save changes no paper timestamp, history or shared fields");
        check(edit.Settings.PaperResources[paper.Id].Target == folder, "local binding uses paper ID");
        edit.Settings.ShowResourceLinks = false; edit.Settings.TimeDisplayMode = "hidden";
        check(SyncProtocol.Diff(before, edit).Count == 0 && edit.Settings.PaperResources.Count == 1, "hiding and display choices preserve binding and produce no shared edits");
        var renamed = Storage.Clone(edit.Papers[0]); renamed.Title = "Synthetic renamed";
        PaperEditorChanges.Apply(edit, edit.Papers[0], renamed, local);
        check(edit.Settings.PaperResources[paper.Id].Target == folder, "rename keeps link");
        var copied = Storage.Clone(renamed); copied.Id = Guid.NewGuid().ToString("N"); edit.Papers.Add(copied);
        check(!edit.Settings.PaperResources.ContainsKey(copied.Id), "copy with a new ID has no link");
        var roundtrip = Storage.CloneLibrary(edit);
        check(!roundtrip.Settings.ShowResourceLinks && roundtrip.Settings.TimeDisplayMode == "hidden" && roundtrip.Settings.PaperResources[paper.Id].Target == folder, "local settings and unavailable folder survive restart");
        PaperEditorChanges.Apply(edit, edit.Papers[0], Storage.Clone(edit.Papers[0]), null);
        check(edit.Settings.PaperResources.Count == 0, "clear removes local binding");
        var stale = Storage.Clone(paper); edit.Papers.RemoveAll(p => p.Id == paper.Id);
        PaperEditorChanges.Apply(edit, paper, stale, local);
        check(!edit.Papers.Any(p => p.Id == paper.Id) && !edit.Settings.PaperResources.ContainsKey(paper.Id), "late editor cannot resurrect deletion or binding");

        var list = new List<Paper>
        {
            new() { Title = "No date", Priority = "高" },
            new() { Title = "Future same 1", DueDate = today.AddDays(3), Priority = "低" },
            new() { Title = "Today", DueDate = today, Priority = "中" },
            new() { Title = "Overdue", DueDate = today.AddDays(-1), Priority = "低" },
            new() { Title = "Future same 2", DueDate = today.AddDays(3).AddHours(9), Priority = "高" },
            new() { Title = "Near", DueDate = today.AddDays(1), Priority = "中" },
            new() { Title = "Complete", DueDate = today.AddDays(-9) }
        };
        foreach (var stage in list[^1].Stages) stage.Done = true;
        var manualIds = list.Select(p => p.Id).ToArray();
        check(ViewRules.Order(list, ViewRules.SortModes[2]).Select(p => p.Title).SequenceEqual(new[] { "Overdue", "Today", "Near", "Future same 1", "Future same 2", "No date", "Complete" }), "date sort is stable within calendar day and excludes completion");
        check(list.Select(p => p.Id).SequenceEqual(manualIds) && ViewRules.Order(list, ViewRules.SortModes[0]).SequenceEqual(list), "automatic sorting never rewrites manual order");
        var prefs = new Preferences { SortMode = ViewRules.SortModes[2], PageMode = ViewRules.PageModes[1], PageIndex = 2, TimeDisplayMode = "hidden" };
        check(ViewRules.PageCount(prefs) == 1 && ViewRules.Apply(list, prefs).Count == list.Count, "date sorting disables priority paging even when times hidden");
        prefs.TagHidingEnabled = true; prefs.HiddenTags.Add("hidden"); list[3].Tags.Add("hidden");
        check(!ViewRules.Apply(list, prefs).Contains(list[3]), "date sort honours hidden tags");
        prefs.VisiblePriorities = new() { "低" };
        check(ViewRules.Apply(list, prefs).Single() == list[1], "date sort honours explicit priority filter");
        var sortedLibrary = new Library { Papers = list, Settings = prefs };
        var saved = Storage.CloneLibrary(sortedLibrary);
        check(saved.Settings.SortMode == ViewRules.SortModes[2] && saved.Settings.PageMode == ViewRules.PageModes[0] && saved.Settings.PageIndex == 0, "date sort persists and normalizes paging on restart");

        // Two isolated devices share only due dates and ordinary paper events.
        var seed = new Library { Papers = new() { new Paper { Title = "Synthetic sync boundary", StartDate = today } } };
        var shared = Path.Combine(root, "shared");
        var a = new SyncEngine(Path.Combine(root, "a"), shared, seed);
        var b = new SyncEngine(Path.Combine(root, "b"), shared, new Library());
        a.Poll(); b.Poll();
        var current = a.Snapshot(); var candidate = Storage.CloneLibrary(current);
        PaperEditorChanges.Apply(candidate, current.Papers[0], Storage.Clone(current.Papers[0]), local);
        int journalCount = Directory.GetFiles(Path.Combine(root, "a", "journal"), "*.json").Length;
        a.Commit(current, candidate);
        check(Directory.GetFiles(Path.Combine(root, "a", "journal"), "*.json").Length == journalCount, "local resource commit writes no shared event");
        var draft = Storage.Clone(candidate.Papers[0]); draft.DueDate = today.AddDays(5);
        var previous = Storage.CloneLibrary(candidate);
        PaperEditorChanges.Apply(candidate, previous.Papers[0], draft, local); a.Commit(previous, candidate); a.Poll(); b.Poll();
        check(b.Snapshot().Papers.Single().DueDate == today.AddDays(5) && b.Snapshot().Papers[0].StartDate == today, "deadline syncs with start date unchanged");
        check(b.Snapshot().Settings.PaperResources.Count == 0, "other computer never receives a local link");
        var eventText = string.Join("", Directory.GetFiles(Path.Combine(shared, "events-v1"), "*.json").Select(File.ReadAllText));
        check(!eventText.Contains("PaperResources") && !eventText.Contains("TimeDisplayMode") && !eventText.Contains("Target") && !eventText.Contains(root), "shared event files contain no link path or local display preferences");
        Lang.Apply(Lang.English);
        check(ViewRules.TimeChoices().Select(c => c.Value).SequenceEqual(ViewRules.TimeModes) && ViewRules.TimeChoices().All(c => !c.Label.Any(ch => ch >= 0x4e00 && ch <= 0x9fff)), "time picker uses ASCII codes with translated labels");
        check(ViewRules.SortChoices()[2].Value == "截止日期" && ViewRules.SortChoices()[2].Label == "Countdown first", "date sort display changes without changing canonical storage");
        Lang.Apply(Lang.Chinese);
    }
}
