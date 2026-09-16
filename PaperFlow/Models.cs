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
    public static readonly string[] StageNames = { "开题", "语料&数据整理", "初稿", "自修", "投稿", "返修", "收录" };
    // Stable stored names preserve historical event files; display names may evolve.
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
    [JsonIgnore] public string NextStage => Stages.FindIndex(s => !s.Done && !s.Skipped) is int i && i >= 0 ? StageLabels[i] : "阶段已全部完成";
    [JsonIgnore] public int CurrentStageIndex => Math.Max(0, Stages.FindLastIndex(s => s.Done && !s.Skipped));
    [JsonIgnore] public string EffectiveStatus => Stages.Last().Done ? "已录用" : Status;
    [JsonIgnore] public string DeadlineText => DueDate is null ? "" : (DueDate.Value.Date - DateTime.Today).Days switch
    {
        < 0 => $"已逾期 {(DateTime.Today - DueDate.Value.Date).Days} 天",
        0 => "今天截止",
        1 => "明天截止",
        var days => $"还剩 {days} 天"
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
        Record($"{(done ? "完成" : "撤销完成")} · {StageLabels[index]}");
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
    public string SyncFolder { get; set; } = "";
    public string LauncherPath { get; set; } = "";
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
