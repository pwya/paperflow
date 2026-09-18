using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

// 2.0.0 新功能的角标：告诉老用户"新东西在这几处"。规则只有两条——
// 全新安装不亮（新用户没有"新"这个概念，直接记成已见）；升级上来的、还没点掉的才亮。
// 消除的时机是"离开"：切到别的设置分类、或关闭那个窗口，而不是鼠标停上去（太容易误触发），
// 也不是一打开就消（页面还没看清，角标就闪掉了）。
public static class WhatsNew
{
    public const string Options = "options";        // 挂件顶部"论文选项"
    public const string Schemes = "schemes";        // 设置 → 阶段方案
    public const string ViewPage = "view";          // 设置 → 视图与分页
    public const string TagHiding = "tag-hiding";   // 视图与分页 → 隐藏用标签

    public static readonly IReadOnlyList<string> All = new[] { Options, Schemes, ViewPage, TagHiding };

    // 离开这一页时该记掉哪些：视图与分页带着它里面的"隐藏用标签"一起消。
    public static List<string> Leaving(string pageId) =>
        pageId == ViewPage ? new List<string> { ViewPage, TagHiding } : new List<string> { pageId };

    // 只有确实是"新功能"的 id 才谈得上角标；设置里的其它分类（外观、同步……）传进来一律不亮。
    public static bool ShouldShow(string id, bool freshInstall, IEnumerable<string> seen) =>
        All.Contains(id) && !freshInstall && !seen.Contains(id);

    // 全新安装：一次把这些标记都记成已见，以后升级才会再亮。
    public static List<string> SeenAfterFreshInstall(IEnumerable<string> seen) => All.Concat(seen).Distinct(StringComparer.Ordinal).ToList();
}
