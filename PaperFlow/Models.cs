using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PaperFlow;

public sealed class Stage
{
    public string Name { get; set; } = "";
    public bool Done { get; set; }
    public bool Skipped { get; set; }
}

public sealed class Change
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
}

public sealed class Paper
{
    // 内置"标准七步"方案的阶段。新论文默认用它；老数据也是这七个名字。
    public static readonly string[] StageNames = { "开题", "语料&数据整理", "初稿", "自修", "投稿", "返修", "收录" };
    // 老数据的显示名映射（第 5 步存的是"投稿"、显示成"在审"）。新代码用 Schemes.Display。
    public static readonly string[] StageLabels = { "开题", "语料&数据整理", "初稿", "自修", "在审", "返修", "收录" };
    public static readonly string[] Priorities = { "高", "中", "低" };
    public static readonly string[] Statuses = { "准备中", "写作中", "审稿中", "待返修", "已修回", "已录用", "准备转投", "暂停" };
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Language { get; set; } = "";
    public string Collaborators { get; set; } = "";
    public string Journal { get; set; } = "";
    public string Status { get; set; } = "准备中";
    public string Priority { get; set; } = "中";
    public string NextAction { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Notes { get; set; } = "";
    // 这篇论文来自哪套方案（方案跟着论文走）。名字对不上时按"标准七步"处理。
    public string SchemeName { get; set; } = Schemes.DefaultName;
    // 隐藏用标签：用户自建的短记号，最多三个，用来把论文收起来。
    public List<string> Tags { get; set; } = new();
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool Archived { get; set; }
    public List<Stage> Stages { get; set; } = StageNames.Select(n => new Stage { Name = n }).ToList();
    public List<Change> History { get; set; } = new();

    [JsonIgnore] public int Total => Stages.Count(s => !s.Skipped);
    [JsonIgnore] public int Completed => Stages.Count(s => s.Done && !s.Skipped);
    [JsonIgnore] public int Progress => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed / Total, MidpointRounding.AwayFromZero);
    [JsonIgnore] public bool IsComplete => Total > 0 && Completed == Total;
    [JsonIgnore] public int ElapsedDays => Math.Max(0, (DateTime.Today - StartDate.Date).Days);
    [JsonIgnore] public string NextStage => Stages.FindIndex(s => !s.Done && !s.Skipped) is int i && i >= 0 ? Lang.T(Schemes.Display(Stages[i].Name)) : Lang.T("阶段已全部完成");
    [JsonIgnore] public int CurrentStageIndex => Math.Max(0, Stages.FindLastIndex(s => s.Done && !s.Skipped));
    [JsonIgnore] public string EffectiveStatus => Stages.Last().Done ? "已录用" : Status;
    [JsonIgnore] public string DeadlineText => DueDate is null ? "" : (DueDate.Value.Date - DateTime.Today).Days switch
    {
        < 0 => Lang.P((DateTime.Today - DueDate.Value.Date).Days, "已逾期 {0} 天", "Overdue by {0} day", "Overdue by {0} days", (DateTime.Today - DueDate.Value.Date).Days),
        0 => Lang.T("今天截止"),
        1 => Lang.T("明天截止"),
        var days => Lang.P(days, "还剩 {0} 天", "{0} day left", "{0} days left", days)
    };

    public void Record(string message)
    {
        UpdatedAt = DateTime.Now;
        History.Insert(0, new Change { At = UpdatedAt, Description = message });
    }

    public void ToggleStage(int index, bool done)
    {
        var stage = Stages[index];
        if (stage.Skipped || stage.Done == done) return;
        stage.Done = done;
        Record($"{(done ? "完成" : "撤销完成")} · {Schemes.Display(stage.Name)}");
    }
}

public sealed class Preferences
{
    public bool TitleBold { get; set; } = true;
    public bool HideSelectedStages { get; set; } = true;
    public List<int> HiddenStages { get; set; } = new() { 4 };
    // 临时展开：不改变“隐藏在审”这个长期设置，只是现在看一眼。
    public bool ShowHiddenNow { get; set; }
    public bool ShowNotices { get; set; } = true;
    public string SoundMode { get; set; } = "关";
    public string SoundStyle { get; set; } = "木质";
    public double SoundVolume { get; set; } = 0.6;
    public List<string> VisiblePriorities { get; set; } = new() { "高", "中", "低" };
    public string PageMode { get; set; } = "不翻页";
    // 排序方式是本机偏好，但必须保存下来，否则重启就悄悄回到手动排序。
    public string SortMode { get; set; } = "手动排序";
    public int PageIndex { get; set; }
    public string Theme { get; set; } = Themes.Default;
    public string ListLayout { get; set; } = Themes.CardLayout;
    public bool FollowSystemTheme { get; set; }
    public string AccentColor { get; set; } = "";
    public string BackgroundColor { get; set; } = "";
    public double BackgroundOpacity { get; set; } = 1;
    public string FontName { get; set; } = "Microsoft YaHei UI";
    public double TextSize { get; set; } = 13;
    // 三档文字层级：标题 / 正文 / 次要。字体与颜色留空表示跟随基础设置或主题。
    public string TitleFont { get; set; } = "";
    public double TitleScale { get; set; } = 1;
    public string TitleColor { get; set; } = "";
    public string BodyFont { get; set; } = "";
    public double BodyScale { get; set; } = 1;
    public string BodyColor { get; set; } = "";
    public string CaptionFont { get; set; } = "";
    public double CaptionScale { get; set; } = 1;
    public string CaptionColor { get; set; } = "";
    // Overall zoom of the widget (layout and text together); TextSize only moves the text.
    public double UiScale { get; set; } = 1;
    public bool AutoGrowWindow { get; set; }
    // 背景图片只在本机使用，路径不参与同步。
    public string BackgroundImage { get; set; } = "";
    public double ImageScrim { get; set; } = 0.35;
    // 自建的阶段方案。内置两套写在代码里，不占数据；这里的都是用户自己另存出来的。
    public List<StageScheme> CustomSchemes { get; set; } = new();
    // 隐藏用标签：总开关默认关，标签默认一个都不预置。
    public bool TagHidingEnabled { get; set; }
    public List<string> CustomTags { get; set; } = new();
    public List<string> HiddenTags { get; set; } = new();
    public string SyncFolder { get; set; } = "";
    public string LauncherPath { get; set; } = "";
    // 界面语言：auto 看 Windows 显示语言，zh / en 是手动指定。只影响显示。
    public string Language { get; set; } = Lang.Auto;
    // 更新检查：always / daily / never，默认每天一次。只检查、不联网上传任何东西。
    public string UpdateMode { get; set; } = "daily";
    public DateTime? LastUpdateCheckUtc { get; set; }
    // 上一次检查为什么没成功（空字符串表示上次是成功的）。只存本机，给设置页看。
    public string LastUpdateError { get; set; } = "";
    // 刚装完的更新：重启后把这一版改了什么再显示一次。只在本机，不参与同步。
    public string PendingReleaseVersion { get; set; } = "";
    public string PendingReleaseNotes { get; set; } = "";
    // 已经"看过"的新功能角标（本机记录，不参与同步）：全新安装会一次记满。
    public List<string> SeenNewFeatures { get; set; } = new();
    public bool Topmost { get; set; } = true;
    public bool Compact { get; set; }
    public int BarHeight { get; set; } = 20;
    public double Width { get; set; } = 650;
    public double Height { get; set; } = 840;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
}

public sealed class Library
{
    public int Version { get; set; } = 1;
    public Preferences Settings { get; set; } = new();
    public List<Paper> Papers { get; set; } = new();
}
