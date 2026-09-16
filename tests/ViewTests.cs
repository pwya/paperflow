using PaperFlow;

static class ViewTests
{
    public static void Run(Action<bool, string> check)
    {
        var writing = new Paper { Title = "Synthetic writing", Priority = "高" }; writing.Stages[2].Done = true;
        var review = new Paper { Title = "Synthetic review", Priority = "中" }; review.Stages[4].Done = true;
        var revision = new Paper { Title = "Synthetic revision", Priority = "低" }; revision.Stages[4].Done = true; revision.Stages[5].Done = true;
        var accepted = new Paper { Title = "Synthetic accepted", Priority = "高" }; accepted.Stages[4].Done = true; accepted.Stages[6].Done = true;
        var all = new[] { writing, review, revision, accepted }; var p = new Preferences();
        check(Paper.StageLabels[4] == "在审" && Paper.StageNames[4] == "投稿", "renamed UI keeps stable historical stage identity");
        check(review.CurrentStageIndex == 4 && revision.CurrentStageIndex == 5 && accepted.CurrentStageIndex == 6, "later stages leave review bucket");
        check(Paper.Priorities.Select(ViewRules.PriorityLevel).SequenceEqual(new[] { 3, 2, 1 }), "title dots fill three, two or one for high, medium and low");
        check(ViewRules.PriorityLevel("") == 2 && ViewRules.PriorityLevel("高") == 3 && ViewRules.PriorityLevel("低") == 1, "dot count survives unknown and legacy values");
        check(new Preferences().UiScale == 1 && !new Preferences().AutoGrowWindow, "new appearance settings default to 100% zoom and no auto grow");
        check(ViewRules.EstimateVisiblePapers(600, 120, 4) == 5 && ViewRules.EstimateVisiblePapers(500, 120, 4) == 4, "visible estimate divides the viewport by the measured card");
        check(ViewRules.EstimateVisiblePapers(100, 120, 4) == 0 && ViewRules.EstimateVisiblePapers(600, 0, 4) == 0 && ViewRules.EstimateVisiblePapers(600, 120, 0) == 0, "visible estimate refuses impossible input");
        var extreme = Storage.Parse("{\"Version\":1,\"Settings\":{\"TextSize\":99,\"UiScale\":9},\"Papers\":[]}");
        check(extreme.Settings.TextSize == 36 && extreme.Settings.UiScale == 2, "font size and zoom are clamped when loading");
        check(Themes.All.Length >= 20, "the theme catalogue ships a full set of options");
        check(Themes.All.Select(t => t.Name).Distinct().Count() == Themes.All.Length, "theme names are unique");
        check(Themes.All.All(t => t.Pair == "" || Themes.All.Any(o => o.Name == t.Pair)), "every light/dark pair points at a real theme");
        check(Themes.Grouped().Count() >= 5, "themes are grouped into families for the picker");
        check(Themes.Find("竹青").Name == "经典 · 竹青" && Themes.Find("夜墨").Dark, "legacy theme names migrate to the classic family");
        check(Themes.Find("不存在的主题").Name == Themes.Default, "an unknown theme falls back to the default");
        check(Themes.Follow(Themes.Find("纸感 · 竹青"), true).Name == "夜航 · 霜蓝" && Themes.Follow(Themes.Find("夜航 · 霜蓝"), false).Name == "纸感 · 竹青", "follow-system swaps between the paired light and dark themes");
        var stored = Storage.Parse("{\"Version\":1,\"Settings\":{\"Theme\":\"暖杏\",\"ListLayout\":\"乱七八糟\",\"BarHeight\":33},\"Papers\":[]}");
        check(stored.Settings.Theme == "经典 · 暖杏" && stored.Settings.ListLayout == Themes.CardLayout && stored.Settings.BarHeight == 0, "stored settings migrate the theme and clamp layout and bar height");
        check(new Preferences().Theme == Themes.Default && new Preferences().ListLayout == Themes.CardLayout, "new installs start on the redesigned default");
        check(Themes.IsHex("#0F766E") && !Themes.IsHex("red") && !Themes.IsHex("") && !Themes.IsHex("#12345"), "hex validation rejects anything that is not six digits");
        check(Themes.ContrastRatio("#000000", "#FFFFFF") > 20 && Math.Abs(Themes.ContrastRatio("#777777", "#777777") - 1) < 0.001, "contrast ratio maths matches the usual definition");
        var tiers = Storage.Parse("{\"Version\":1,\"Settings\":{\"TitleFont\":\"   \",\"TitleColor\":\"red\",\"BodyColor\":\"#123456\",\"CaptionScale\":9,\"BodyScale\":0.1},\"Papers\":[]}");
        check(tiers.Settings.TitleFont == "" && tiers.Settings.TitleColor == "", "blank tier fonts fall back and invalid colours are dropped");
        check(tiers.Settings.BodyColor == "#123456" && tiers.Settings.CaptionScale == 2 && tiers.Settings.BodyScale == 0.6, "tier colours survive and scales are clamped to 60-200%");
        check(new Preferences().TitleScale == 1 && new Preferences().CaptionFont == "" && new Preferences().BodyColor == "", "tier defaults follow the base font and the theme");
        check(new Preferences().ShowNotices, "operation notices are on by default");
        // 勾选阶段之后的交代：留在原地不提示，被隐藏或被分页才提示。
        var plain = new Preferences { HideSelectedStages = true, HiddenStages = new() { 4 } };
        var moving = new Paper { Title = "Synthetic move", Priority = "中" }; moving.Stages[2].Done = true;
        var list = new List<Paper> { moving };
        check(ViewRules.AfterStageToggle(moving, plain, list) == null, "no notice while the card stays put");
        moving.Stages[4].Done = true;
        var hidden = ViewRules.AfterStageToggle(moving, plain, list);
        check(hidden != null && hidden.Kind == "hidden" && hidden.Action == "立即显示" && hidden.Text.Contains("在审"), "checking a hidden stage explains where the paper went");
        var grouped = new Preferences { HideSelectedStages = true, HiddenStages = new() { 4 }, PageMode = ViewRules.PageModes[2], PageIndex = 0 };
        var paged = ViewRules.AfterStageToggle(moving, grouped, list);
        check(paged != null && paged.Kind == "paged" && paged.Page == 1 && paged.Action.Contains("第 2 页"), "stage-grouped paging offers to jump to the page that holds it");
        moving.Stages[6].Done = true;
        var acceptedHidden = new Preferences { HideSelectedStages = true, HiddenStages = new() { 6 } };
        var later = ViewRules.AfterStageToggle(moving, acceptedHidden, list);
        check(later != null && later.Kind == "hidden" && later.Text.Contains("收录"), "later stages are still explained when hidden");
        check(ViewRules.Apply(all, p).Select(x => x.Id).SequenceEqual(new[] { writing.Id, revision.Id, accepted.Id }), "review hidden by default, revision and accepted remain");
        p.HiddenStages.Add(6); check(ViewRules.Apply(all, p).Count == 2, "multiple hidden stages");
        p.HideSelectedStages = false; check(ViewRules.Apply(all, p).Count == 4, "hiding can be disabled");
        p.VisiblePriorities = new() { "高", "低" }; check(ViewRules.Apply(all, p).Count == 3, "multi priority filter");
        p.VisiblePriorities.Clear(); check(ViewRules.Apply(all, p).Count == 0, "no selected priority is explicit empty result");
        p.VisiblePriorities = Paper.Priorities.ToList(); p.PageMode = ViewRules.PageModes[1];
        check(ViewRules.PageCount(p) == 3, "priority has three pages");
        check(ViewRules.Apply(all, p, 0).All(x => x.Priority == "高") && ViewRules.Apply(all, p, 0).Count == 2, "priority page high");
        check(ViewRules.Apply(all, p, 1).Single() == review, "priority page medium");
        check(ViewRules.Apply(all, p, 2).Single() == revision, "priority page low");
        p.HideSelectedStages = true; check(ViewRules.Apply(all, p, 1).Count == 0, "stage hiding intersects priority pages");
        p.PageMode = ViewRules.PageModes[2];
        var front = ViewRules.Apply(all, p, 0); var back = ViewRules.Apply(all, p, 1);
        check(front.Count == 2 && back.Count == 2 && front.Concat(back).Select(x => x.Id).Distinct().Count() == 4, "stage pages partition without loss or duplication");
        check(back.Contains(review) && back.Contains(accepted), "selected-stage page ignores hide toggle and reveals selected records");
        p.VisiblePriorities = new() { "高" }; check(ViewRules.Apply(all, p, 1).Single() == accepted, "priority filter intersects stage pages");
        p.VisiblePriorities = Paper.Priorities.ToList(); p.HiddenStages.Clear();
        check(ViewRules.Apply(all, p, 0).Count == 4 && ViewRules.Apply(all, p, 1).Count == 0, "empty hidden-stage selection gives empty second page");
        p.HiddenStages.Add(0); check(ViewRules.SelectedStage(new Paper(), p), "unstarted papers grouped as opening stage");
        var old = Storage.Parse("{\"Version\":1,\"Papers\":[{\"Title\":\"Legacy sample\"}]}");
        check(old.Papers.Single().Priority == "中" && old.Settings.TitleBold && old.Settings.HiddenStages.SequenceEqual(new[] { 4 }), "legacy defaults for new settings and priority");
        check(old.Settings.UiScale == 1 && !old.Settings.AutoGrowWindow && old.Settings.TextSize == 13, "legacy files get 100% zoom, no auto grow and the default font size");
        old.Settings.TitleBold = false; old.Settings.PageMode = ViewRules.PageModes[1]; old.Settings.PageIndex = 2;
        var copy = Storage.CloneLibrary(old); check(!copy.Settings.TitleBold && copy.Settings.PageIndex == 2, "title weight and page survive save roundtrip");
        old.Papers[0].Priority = "invalid"; bool rejected = false; try { Storage.Validate(old); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "invalid priority rejected");
        var oldStage = new Paper { Title = "Legacy posted sample" }; oldStage.Stages[4].Done = true;
        var before = new Library { Papers = new() { oldStage } }; var after = Storage.CloneLibrary(before);
        check(SyncProtocol.Diff(before, after).Count == 0 && after.Papers[0].Progress == 14, "display rename neither rewrites old events nor changes progress");
    }
}
