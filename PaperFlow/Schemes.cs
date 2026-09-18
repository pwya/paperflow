using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

// 一套方案 = 一个名字 + 一串阶段名。内置两套写在代码里（不占数据、不能改名删），
// 用户自建的方案存在本机偏好里；论文自己带着阶段清单的副本和方案名，所以换电脑
// 也不会出现"论文用的方案这台电脑没有"。
public sealed record StageScheme(string Name, List<string> StageNames);

// 一套方案为什么不能用。枚举给代码用，文案在 Storage 里映射（那里会被词表扫描盯着）。
public enum SchemeProblem { None, TooFewStages, TooManyStages, EmptyName, NameTooWide, DuplicateName }

public static class Schemes
{
    // 名字长度按显示宽度算：汉字 2、字母数字 1。这样中文用户是"十二个字左右"，
    // 英文用户也能写下 "Revision after review"，卡片不会被撑坏。
    public const int MaxNameWidth = 24;
    public const int MaxTagWidth = 12;
    public const int MinStages = 2;
    public const int MaxStages = 12;
    public const int MaxTags = 3;
    public const int MaxCustomTags = 20;
    public const string DefaultName = "标准七步";
    public const string SixStepName = "六步流程";
    // 内置方案第 5 步存的还是"投稿"，但界面一直显示"在审"（1.x 就如此，不改老用户看到的字）。
    private const string Submission = "投稿";
    private const string UnderReview = "在审";

    public static readonly StageScheme Default = new(DefaultName, Paper.StageNames.ToList());
    public static readonly StageScheme SixStep = new(SixStepName, new List<string> { "准备中", "写作中", "审稿中", "返修中", "已修回", "已录用" });
    public static readonly IReadOnlyList<StageScheme> BuiltIn = new[] { Default, SixStep };

    // East Asian Width 的常用区间：落在里面的算两格宽。
    private static bool IsWide(char c) =>
        (c >= 0x1100 && c <= 0x115F) || c == 0x2329 || c == 0x232A ||
        (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||
        (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);

    public static int Width(string? text)
    {
        int width = 0;
        foreach (char c in text ?? "") width += IsWide(c) ? 2 : 1;
        return width;
    }

    private static bool IsClean(string? name, int maxWidth) =>
        !string.IsNullOrWhiteSpace(name) && name == name.Trim() && Width(name) <= maxWidth;

    public static bool IsValidStageName(string? name) => IsClean(name, MaxNameWidth);
    public static bool IsValidTagName(string? name) => IsClean(name, MaxTagWidth);
    public static bool IsValidSchemeName(string? name) => IsClean(name, MaxNameWidth);

    public static bool HasDuplicate(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return names.Any(n => !seen.Add(n));
    }

    // 这套方案本身能不能用；顺便判断一篇论文自己的阶段清单。
    // 只看阶段清单（同步事件里的 stages 走这一条，它不带方案名）。
    public static SchemeProblem InspectStages(IReadOnlyList<string> stages)
    {
        if (stages.Count < MinStages) return SchemeProblem.TooFewStages;
        if (stages.Count > MaxStages) return SchemeProblem.TooManyStages;
        if (stages.Any(s => string.IsNullOrWhiteSpace(s) || s != s.Trim())) return SchemeProblem.EmptyName;
        if (stages.Any(s => !IsValidStageName(s))) return SchemeProblem.NameTooWide;
        if (HasDuplicate(stages)) return SchemeProblem.DuplicateName;
        return SchemeProblem.None;
    }

    public static SchemeProblem Inspect(string name, IReadOnlyList<string> stages)
    {
        var problem = InspectStages(stages);
        if (problem != SchemeProblem.None) return problem;
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim()) return SchemeProblem.EmptyName;
        return IsValidSchemeName(name) ? SchemeProblem.None : SchemeProblem.NameTooWide;
    }

    public static SchemeProblem Inspect(StageScheme scheme) => Inspect(scheme.Name, scheme.StageNames);

    public static bool IsBuiltIn(string name) => BuiltIn.Any(s => s.Name == name);

    public static StageScheme? Find(string name) => BuiltIn.FirstOrDefault(s => s.Name == name);

    // 内置两套 + 用户自建的，给下拉和面板用。
    public static List<StageScheme> All(IEnumerable<StageScheme> custom) => BuiltIn.Concat(custom).ToList();

    // 这套方案里有没有这个名字（用来判断"改过没有"）。
    public static bool Matches(StageScheme scheme, IEnumerable<string> stages) => scheme.StageNames.SequenceEqual(stages);

    // 这篇论文现在用的是哪套方案：名字对得上、内容一字不差才算没改过；否则它就是自己单独改过的。
    public static (StageScheme Scheme, bool Changed) Resolve(Paper paper, IEnumerable<StageScheme> custom)
    {
        string name = string.IsNullOrWhiteSpace(paper.SchemeName) ? DefaultName : paper.SchemeName;
        var stages = paper.Stages.Select(s => s.Name).ToList();
        var found = BuiltIn.Concat(custom).FirstOrDefault(s => s.Name == name);
        return found != null && Matches(found, stages) ? (found, false) : (new StageScheme(name, stages), true);
    }

    // 拖动排序：把第 from 个搬到第 to 个位置。下标越界就原样返回，不抛异常。
    public static List<string> Move(IReadOnlyList<string> stages, int from, int to)
    {
        var list = stages.ToList();
        if (from < 0 || from >= list.Count) return list;
        string item = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), item);
        return list;
    }

    // 本机方案库里没有、但论文身上带着的方案：多半来自另一台电脑。
    // 取第一篇用它的论文的阶段清单当这套方案（同一套方案的论文本来就应该一致）。
    public static List<StageScheme> FromPapers(IEnumerable<Paper> papers, IEnumerable<StageScheme> known)
    {
        var have = All(known).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var found = new List<StageScheme>();
        foreach (var paper in papers)
        {
            string name = paper.SchemeName;
            if (string.IsNullOrWhiteSpace(name) || have.Contains(name) || found.Any(s => s.Name == name)) continue;
            found.Add(new StageScheme(name, paper.Stages.Select(s => s.Name).ToList()));
        }
        return found;
    }

    // 这套方案现在有多少篇论文在用、其中多少篇已经和方案不一致（单独改过）。
    public static (int Using, int Edited) Usage(IEnumerable<Paper> papers, string schemeName, IEnumerable<StageScheme> known)
    {
        var mine = papers.Where(p => p.SchemeName == schemeName).ToList();
        var scheme = All(known).FirstOrDefault(s => s.Name == schemeName);
        return (mine.Count, scheme == null ? 0 : mine.Count(p => !Matches(scheme, p.Stages.Select(s => s.Name))));
    }

    // 把方案的新阶段推到正在用它的论文上：按名字保留勾选，返回改了几篇。
    // oldStages 是"改之前"的方案阶段，用来判断哪几篇是用户单独改过的（不能拿改完的新方案去比）。
    public static int PushToPapers(IEnumerable<Paper> papers, string schemeName, IReadOnlyList<string> oldStages, IReadOnlyList<string> newStages, bool onlyUnchanged)
    {
        var before = new StageScheme(schemeName, oldStages.ToList());
        int changed = 0;
        foreach (var paper in papers.Where(p => p.SchemeName == schemeName).ToList())
        {
            if (onlyUnchanged && !Matches(before, paper.Stages.Select(s => s.Name))) continue;
            paper.Stages = Switch(paper.Stages, newStages);
            changed++;
        }
        return changed;
    }

    // 汇总里"X 篇……"那半句用的名字：所有论文最后一格同名就用它，不然退回到中性的"已完成"。
    public static string CompletionLabel(IEnumerable<Paper> papers)
    {
        var names = papers.Select(p => p.Stages.Count == 0 ? "" : Display(p.Stages[^1].Name)).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return names.Count == 1 ? names[0] : "已完成";
    }

    // 卡片上放几个标签：标题先留够 titleFloor 的宽度，剩下的宽度按每个标签占 tagWidth 算。
    // 抽成纯函数是为了能断言"标签优先于标题"这条规矩。
    public static int TagSlots(int tagCount, double roomWidth, double tagWidth, double titleFloor)
    {
        if (tagCount <= 0 || tagWidth <= 0) return 0;
        double room = roomWidth - titleFloor;
        if (room <= 0) return 0;
        return Math.Clamp((int)(room / (tagWidth + 14)), 0, Math.Min(tagCount, MaxTags));
    }

    // 显示名：内置的"投稿"在界面上叫"在审"，自建阶段原样显示。
    public static string Display(string storedName) => storedName == Submission ? UnderReview : storedName;

    // 一篇论文当前的阶段清单（配合它自己的名字用）。
    public static (string Name, List<string> Stages) Of(Paper paper) =>
        (string.IsNullOrWhiteSpace(paper.SchemeName) ? DefaultName : paper.SchemeName, paper.Stages.Select(s => s.Name).ToList());

    public static List<Stage> NewStages(IEnumerable<string> names) => names.Select(n => new Stage { Name = n }).ToList();

    // 换方案：按名字保留勾选与"不适用"，新方案独有的阶段从"未勾"开始。
    // 对不上的名字直接丢——绝不按位置硬套，那样会凭空造出用户没打过的勾。
    public static List<Stage> Switch(IEnumerable<Stage> current, IEnumerable<string> target)
    {
        var before = current.ToDictionary(s => s.Name, StringComparer.Ordinal);
        return target.Select(name => before.TryGetValue(name, out var old)
            ? new Stage { Name = name, Done = old.Done, Skipped = old.Skipped }
            : new Stage { Name = name }).ToList();
    }

    // 换方案时会丢多少勾，用来决定要不要弹确认。
    public static (int Kept, int Lost) Match(IEnumerable<Stage> current, IEnumerable<string> target)
    {
        var kept = current.Where(s => target.Contains(s.Name, StringComparer.Ordinal)).ToList();
        return (kept.Count(s => s.Done || s.Skipped), current.Count(s => (s.Done || s.Skipped) && !target.Contains(s.Name, StringComparer.Ordinal)));
    }
}
