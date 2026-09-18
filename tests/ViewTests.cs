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
        check(new Preferences().SortMode == "手动排序", "sorting starts on the manual order");
        var sorted = Storage.Parse("{\"Version\":1,\"Settings\":{\"SortMode\":\"按心情\"},\"Papers\":[]}");
        check(sorted.Settings.SortMode == "手动排序", "an unknown sort mode falls back to manual order");
        var kept = Storage.Parse("{\"Version\":1,\"Settings\":{\"SortMode\":\"最近修改\"},\"Papers\":[]}");
        check(kept.Settings.SortMode == "最近修改", "a saved sort mode survives a reload");
        check(Themes.IsHex("#0F766E") && !Themes.IsHex("red") && !Themes.IsHex("") && !Themes.IsHex("#12345"), "hex validation rejects anything that is not six digits");
        check(Themes.ContrastRatio("#000000", "#FFFFFF") > 20 && Math.Abs(Themes.ContrastRatio("#777777", "#777777") - 1) < 0.001, "contrast ratio maths matches the usual definition");
        var tiers = Storage.Parse("{\"Version\":1,\"Settings\":{\"TitleFont\":\"   \",\"TitleColor\":\"red\",\"BodyColor\":\"#123456\",\"CaptionScale\":9,\"BodyScale\":0.1},\"Papers\":[]}");
        check(tiers.Settings.TitleFont == "" && tiers.Settings.TitleColor == "", "blank tier fonts fall back and invalid colours are dropped");
        check(tiers.Settings.BodyColor == "#123456" && tiers.Settings.CaptionScale == 2 && tiers.Settings.BodyScale == 0.6, "tier colours survive and scales are clamped to 60-200%");
        check(new Preferences().TitleScale == 1 && new Preferences().CaptionFont == "" && new Preferences().BodyColor == "", "tier defaults follow the base font and the theme");
        check(new Preferences().ShowNotices, "operation notices are on by default");
        check(new Preferences().SoundMode == ViewRules.SoundModes[0] && new Preferences().SoundStyle == "木质", "sound stays off until the user turns it on");
        check(ViewRules.SoundFor("关", true, false) == null, "silent mode never plays");
        check(ViewRules.SoundFor("只完成时", false, false) == null && ViewRules.SoundFor("只完成时", true, false) == "complete", "completion-only mode stays quiet on undo");
        check(ViewRules.SoundFor("完成和取消都响", false, false) == "undo", "the louder mode also marks undo");
        check(ViewRules.SoundFor("完成和取消都响", true, true) == "reward", "finishing every stage plays the reward chime");
        var quiet = Storage.Parse("{\"Version\":1,\"Settings\":{\"SoundMode\":\"乱填\",\"SoundStyle\":\"乱填\",\"SoundVolume\":9},\"Papers\":[]}");
        check(quiet.Settings.SoundMode == "关" && quiet.Settings.SoundStyle == "木质" && quiet.Settings.SoundVolume == 1, "unknown sound settings fall back and volume is clamped");
        // 2.0.0：收起论文只认"隐藏用标签"，按阶段隐藏和按阶段分组翻页都没有了。
        check(ViewRules.PageModes.Length == 2 && !ViewRules.PageModes.Contains("按阶段分组翻页"), "paging modes only keep one page and priority");
        var plain = new Preferences { TagHidingEnabled = true, HiddenTags = new() { "等老师反馈" } };
        var waiting = new Paper { Title = "Synthetic waiting", Priority = "中" }; waiting.Tags.Add("等老师反馈");
        var waitingList = new List<Paper> { waiting };
        check(ViewRules.HiddenByTag(waiting, plain) && ViewRules.HiddenCount(waitingList, plain) == 1, "a paper carrying a hidden tag is counted");
        check(!ViewRules.HiddenByTag(waiting, new Preferences { TagHidingEnabled = false, HiddenTags = new() { "等老师反馈" } }), "the master switch turns tag hiding off");
        check(!ViewRules.HiddenByTag(new Paper(), plain), "papers without the tag stay visible");
        var notice = ViewRules.AfterTagChange(waiting, plain);
        check(notice != null && notice.Text.Contains("等老师反馈") && notice.Action == "立即显示", "tagging explains where the paper went and offers to reveal it");
        check(ViewRules.AfterTagChange(waiting, new Preferences { TagHidingEnabled = false, HiddenTags = new() { "等老师反馈" } }) == null, "no notice when the tag hides nothing");
        var peekTags = new Preferences { TagHidingEnabled = true, HiddenTags = new() { "等老师反馈" }, ShowHiddenNow = true };
        check(ViewRules.AfterTagChange(waiting, peekTags) == null && ViewRules.Apply(new[] { writing, waiting }, peekTags).Count == 2, "the temporary peek keeps quiet and shows everything");
        check(ViewRules.Apply(new[] { writing, waiting }, plain).Single().Id == writing.Id, "hidden-by-tag papers drop out of the list");
        check(peekTags.HiddenTags.SequenceEqual(new[] { "等老师反馈" }) && peekTags.ShowHiddenNow, "the peek leaves the long-term setting alone");
        plain.HiddenTags.Add("没人提过的"); check(ViewRules.Apply(new[] { writing, waiting }, plain).Count == 1, "tags that nothing carries hide nothing");
        p.VisiblePriorities = new() { "高", "低" }; check(ViewRules.Apply(all, p).Count == 3, "multi priority filter");
        p.VisiblePriorities.Clear(); check(ViewRules.Apply(all, p).Count == 0, "no selected priority is explicit empty result");
        p.VisiblePriorities = Paper.Priorities.ToList(); p.PageMode = ViewRules.PageModes[1];
        check(ViewRules.PageCount(p) == 3, "priority has three pages");
        check(ViewRules.Apply(all, p, 0).All(x => x.Priority == "高") && ViewRules.Apply(all, p, 0).Count == 2, "priority page high");
        check(ViewRules.Apply(all, p, 1).Single() == review, "priority page medium");
        check(ViewRules.Apply(all, p, 2).Single() == revision, "priority page low");
        p.PageMode = ViewRules.PageModes[0];
        check(ViewRules.PageCount(p) == 1 && ViewRules.Apply(all, p).Count == 4, "one page shows everything that is not hidden");
        var old = Storage.Parse("{\"Version\":1,\"Papers\":[{\"Title\":\"Legacy sample\"}]}");
        check(old.Papers.Single().Priority == "中" && old.Settings.TitleBold, "legacy defaults for new settings and priority");
        check(old.Settings.HiddenStages.SequenceEqual(new[] { 4 }), "the 1.13 stage-hiding field is kept untouched in the file");
        check(!ViewRules.HiddenByTag(new Paper(), old.Settings), "the old stage-hiding field no longer hides anything");
        var groupedLegacy = Storage.Parse("{\"Version\":1,\"Settings\":{\"PageMode\":\"按阶段分组翻页\"},\"Papers\":[]}");
        check(groupedLegacy.Settings.PageMode == "不翻页", "an old stage-grouped page mode falls back to one page");
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
