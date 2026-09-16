using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

public static class ViewRules
{
    // 勾选阶段之后给用户的交代：论文还在眼前、被隐藏、还是被挪到了别的页。
    public sealed record StageNotice(string Kind, int Page, string Text, string Action);

    public static readonly string[] PageModes = { "不翻页", "按优先级翻页", "按阶段分组翻页" };
    // The card shows priority as three dots: high fills all three, low fills one.
    public static int PriorityLevel(string priority) => priority switch { "高" => 3, "中" => 2, "低" => 1, _ => 2 };
    // Used by the settings sliders to warn how many whole cards still fit on screen.
    public static int EstimateVisiblePapers(double viewportHeight, double cardHeight, int cardCount)
    {
        if (cardCount <= 0 || cardHeight <= 1 || viewportHeight <= 1) return 0;
        return Math.Clamp((int)Math.Floor(viewportHeight / cardHeight), 0, 999);
    }
    public static int PageCount(Preferences p) => p.PageMode == PageModes[1] ? 3 : p.PageMode == PageModes[2] ? 2 : 1;
    public static bool SelectedStage(Paper paper, Preferences p) => p.HiddenStages.Contains(paper.CurrentStageIndex);
    public static List<Paper> Apply(IEnumerable<Paper> source, Preferences p, int? page = null)
    {
        int index = Math.Clamp(page ?? p.PageIndex, 0, PageCount(p) - 1);
        var papers = source.Where(x => p.VisiblePriorities.Contains(x.Priority));
        if (p.PageMode == PageModes[2]) papers = papers.Where(x => SelectedStage(x, p) == (index == 1));
        else
        {
            if (p.HideSelectedStages) papers = papers.Where(x => !SelectedStage(x, p));
            if (p.PageMode == PageModes[1]) papers = papers.Where(x => x.Priority == Paper.Priorities[index]);
        }
        return papers.ToList();
    }
    public static string PageTitle(Preferences p, int? page = null)
    {
        int index = Math.Clamp(page ?? p.PageIndex, 0, PageCount(p) - 1);
        if (p.PageMode == PageModes[1]) return Paper.Priorities[index] + "优先级";
        if (p.PageMode == PageModes[2]) return index == 0 ? "当前推进" : "所选阶段";
        return "论文列表";
    }
    public static StageNotice? AfterStageToggle(Paper paper, Preferences p, IEnumerable<Paper> candidates)
    {
        var source = candidates.ToList();
        int current = Math.Clamp(p.PageIndex, 0, PageCount(p) - 1);
        if (Apply(source, p, current).Any(x => x.Id == paper.Id)) return null;
        string label = Paper.StageLabels[Math.Clamp(paper.CurrentStageIndex, 0, Paper.StageLabels.Length - 1)];
        for (int i = 0; i < PageCount(p); i++)
        {
            if (i == current) continue;
            if (Apply(source, p, i).Any(x => x.Id == paper.Id))
                return new StageNotice("paged", i, $"已进入{label} · 它被放到了第 {i + 1} 页", $"翻到第 {i + 1} 页");
        }
        if (p.HideSelectedStages && SelectedStage(paper, p))
            return new StageNotice("hidden", -1, $"已进入{label} · 按当前设置，这类论文被隐藏了", "立即显示");
        return null;
    }
}
