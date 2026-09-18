using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

public static class ViewRules
{
    // 贴上一个会隐藏的标签之后给用户的交代。
    public sealed record HideNotice(string Text, string Action);

    // 2.0.0 起没有"按阶段分组翻页"了：翻页只剩不翻页和按优先级。
    public static readonly string[] PageModes = { "不翻页", "按优先级翻页" };
    public static readonly string[] SortModes = { "手动排序", "最近修改", "截止日期", "进度优先" };
    public static readonly string[] SoundModes = { "关", "只完成时", "完成和取消都响" };
    public static readonly string[] SoundStyles = { "木质", "清脆", "水滴" };
    // 该不该响、响哪一种。null 表示不响。
    public static string? SoundFor(string mode, bool done, bool allDone)
    {
        if (mode == SoundModes[0]) return null;
        if (!done && mode != SoundModes[2]) return null;
        if (done) return allDone ? "reward" : "complete";
        return "undo";
    }
    // The card shows priority as three dots: high fills all three, low fills one.
    public static int PriorityLevel(string priority) => priority switch { "高" => 3, "中" => 2, "低" => 1, _ => 2 };
    // Used by the settings sliders to warn how many whole cards still fit on screen.
    public static int EstimateVisiblePapers(double viewportHeight, double cardHeight, int cardCount)
    {
        if (cardCount <= 0 || cardHeight <= 1 || viewportHeight <= 1) return 0;
        return Math.Clamp((int)Math.Floor(viewportHeight / cardHeight), 0, 999);
    }
    public static int PageCount(Preferences p) => p.PageMode == PageModes[1] ? 3 : 1;
    // 收起论文只认"隐藏用标签"：总开关打开，并且这篇论文带着所选的某个标签。
    public static bool HiddenByTag(Paper paper, Preferences p) =>
        p.TagHidingEnabled && p.HiddenTags.Count > 0 && paper.Tags.Any(p.HiddenTags.Contains);
    public static int HiddenCount(IEnumerable<Paper> papers, Preferences p) => papers.Count(x => HiddenByTag(x, p));
    public static List<Paper> Apply(IEnumerable<Paper> source, Preferences p, int? page = null)
    {
        int index = Math.Clamp(page ?? p.PageIndex, 0, PageCount(p) - 1);
        var papers = source.Where(x => p.VisiblePriorities.Contains(x.Priority));
        // 临时展开（ShowHiddenNow）只影响这一次显示，不改动长期设置。
        if (!p.ShowHiddenNow) papers = papers.Where(x => !HiddenByTag(x, p));
        if (p.PageMode == PageModes[1]) papers = papers.Where(x => x.Priority == Paper.Priorities[index]);
        return papers.ToList();
    }
    public static string PageTitle(Preferences p, int? page = null)
    {
        int index = Math.Clamp(page ?? p.PageIndex, 0, PageCount(p) - 1);
        if (p.PageMode == PageModes[1]) return Lang.F("{0}优先级", Lang.Value(Paper.Priorities[index]));
        return Lang.T("论文列表");
    }
    // 贴上标签之后：还在眼前就不吭声；被收起来了就说清楚，并给一个"立即显示"。
    public static HideNotice? AfterTagChange(Paper paper, Preferences p) =>
        HiddenByTag(paper, p) && !p.ShowHiddenNow
            ? new HideNotice(Lang.F("已贴上{0} · 按当前设置，带这个标签的论文被收起来了", string.Join(Lang.ListSeparator, paper.Tags.Where(p.HiddenTags.Contains))), Lang.T("立即显示"))
            : null;
}
