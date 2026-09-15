using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperProgress;

public static class ViewRules
{
    public static readonly string[] PageModes = { "不翻页", "按优先级翻页", "按阶段分组翻页" };
    // The card shows priority as three dots: high fills all three, low fills one.
    public static int PriorityLevel(string priority) => priority switch { "高" => 3, "中" => 2, "低" => 1, _ => 2 };
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
}
