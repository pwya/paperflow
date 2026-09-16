using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

// A theme is a palette plus a shape language. Families decide the shape:
// 纸感 = thin bars, text-only stages, no chip blocks.
// 柔光 = big radii, thick soft bar, pill stages.
// 夜航 = dark, glassy cards, thin bar, pill stages.
// 标签 = compact cards, per-stage colours.
// 极简 = hairline rows for the list layout.
// 经典 = the original six themes, kept so existing installs look unchanged.
public sealed record Theme(
    string Name,
    string Family,
    string Window,
    string Card,
    string Ink,
    string Muted,
    string Accent,
    string Soft,
    string Border,
    bool Dark = false,
    bool Glass = false,
    double CardRadius = 10,
    double ButtonRadius = 8,
    double ChipRadius = 999,
    int BarHeight = 6,
    bool Shadow = false,
    string ChipStyle = "text",
    string HeaderStyle = "plain",
    string Pair = "",
    string[]? Tags = null);

public static class Themes
{
    public const string CardLayout = "卡片";
    public const string ListLayout = "列表";
    public static readonly string[] Layouts = { CardLayout, ListLayout };
    public const string Default = "纸感 · 竹青";

    private static readonly string[] CoolTags = { "#6E7BF2", "#0EA5E9", "#F59E0B", "#10B981", "#6366F1", "#EF4444", "#14B8A6" };
    private static readonly string[] VioletTags = { "#8B5CF6", "#06B6D4", "#F59E0B", "#22C55E", "#6366F1", "#EF4444", "#14B8A6" };
    private static readonly string[] AmberTags = { "#F97316", "#0EA5E9", "#F59E0B", "#10B981", "#6366F1", "#EF4444", "#14B8A6" };

    public static readonly Theme[] All =
    {
        // ---------- 纸感：中性灰白，单一强调色，细进度条，阶段只用圆点加文字 ----------
        new("纸感 · 竹青", "纸感", "#F7F8F8", "#FFFFFF", "#1B2426", "#6C7674", "#0F766E", "#E4F1EE", "#E9EBEA", Shadow: true, Pair: "夜航 · 霜蓝"),
        new("纸感 · 雾蓝", "纸感", "#F6F8FA", "#FFFFFF", "#1C2733", "#6B7684", "#3B6EA5", "#E7EFF7", "#E8ECF1", Shadow: true, Pair: "夜航 · 霜蓝"),
        new("纸感 · 石墨", "纸感", "#F6F6F7", "#FFFFFF", "#1F2124", "#71757A", "#4A5560", "#ECEEF0", "#E7E8EA", Shadow: true, Pair: "极简 · 夜"),
        new("纸感 · 松绿", "纸感", "#F7F9F7", "#FFFFFF", "#1D2520", "#6D7A70", "#2F7D52", "#E5F1E8", "#E6ECE7", Shadow: true, Pair: "夜航 · 苔绿"),

        // ---------- 柔光：米白加暖色，大圆角，厚一点的圆头进度条 ----------
        new("柔光 · 陶土", "柔光", "#FAF6F1", "#FFFFFF", "#3A342E", "#8A8177", "#C2703D", "#F8EADF", "#EFE5DA", CardRadius: 14, ButtonRadius: 10, ChipRadius: 999, BarHeight: 10, Shadow: true, ChipStyle: "pill", HeaderStyle: "outline", Pair: "夜航 · 琥珀"),
        new("柔光 · 米杏", "柔光", "#FBF7F0", "#FFFDF9", "#3B3630", "#8C857A", "#B8873F", "#F6EDDD", "#EFE7DA", CardRadius: 14, ButtonRadius: 10, ChipRadius: 999, BarHeight: 10, Shadow: true, ChipStyle: "pill", HeaderStyle: "outline", Pair: "夜航 · 琥珀"),
        new("柔光 · 橄榄", "柔光", "#F7F8F2", "#FFFFFF", "#333629", "#7C8171", "#6E8B3D", "#EDF1E2", "#E6E9DC", CardRadius: 14, ButtonRadius: 10, ChipRadius: 999, BarHeight: 10, Shadow: true, ChipStyle: "pill", HeaderStyle: "outline", Pair: "夜航 · 苔绿"),
        new("柔光 · 藕荷", "柔光", "#FAF6F8", "#FFFFFF", "#38323A", "#877C88", "#9A5C8A", "#F3E7F0", "#EBE0E8", CardRadius: 14, ButtonRadius: 10, ChipRadius: 999, BarHeight: 10, Shadow: true, ChipStyle: "pill", HeaderStyle: "outline", Pair: "夜航 · 霜蓝"),

        // ---------- 夜航：深色玻璃。霜蓝取自 Nord 官方色值 ----------
        new("夜航 · 霜蓝", "夜航", "#2E3440", "#3B4252", "#ECEFF4", "#9AA5B5", "#88C0D0", "#4C566A", "#434C5E", Dark: true, CardRadius: 12, ChipRadius: 8, BarHeight: 8, ChipStyle: "pill", HeaderStyle: "dark", Pair: "纸感 · 竹青"),
        new("夜航 · 苔绿", "夜航", "#232A31", "#2C343C", "#E8EEF0", "#98A6A8", "#7FB69B", "#37424A", "#3A444C", Dark: true, CardRadius: 12, ChipRadius: 8, BarHeight: 8, ChipStyle: "pill", HeaderStyle: "dark", Pair: "纸感 · 松绿"),
        new("夜航 · 琥珀", "夜航", "#2A2724", "#332F2B", "#F0EBE4", "#A79E93", "#D9A05B", "#413A33", "#463F37", Dark: true, CardRadius: 12, ChipRadius: 8, BarHeight: 8, ChipStyle: "pill", HeaderStyle: "dark", Pair: "柔光 · 陶土"),
        new("夜航 · 靛紫", "夜航", "#262532", "#302E3E", "#EDEBF5", "#9E9AB5", "#A094E8", "#3D3A50", "#3F3C52", Dark: true, CardRadius: 12, ChipRadius: 8, BarHeight: 8, ChipStyle: "pill", HeaderStyle: "dark", Pair: "柔光 · 藕荷"),

        // ---------- 标签：小圆角白卡，阶段按类别分色 ----------
        new("标签 · 蓝", "标签", "#F5F6F8", "#FFFFFF", "#20242B", "#78808C", "#2F6FEB", "#E8F0FE", "#E7E9ED", CardRadius: 8, BarHeight: 8, Shadow: true, ChipStyle: "tag", Pair: "夜航 · 霜蓝", Tags: CoolTags),
        new("标签 · 紫", "标签", "#F6F5F9", "#FFFFFF", "#221F2B", "#7C778C", "#7C4DFF", "#EFEAFE", "#E8E5EF", CardRadius: 8, BarHeight: 8, Shadow: true, ChipStyle: "tag", Pair: "夜航 · 靛紫", Tags: VioletTags),
        new("标签 · 橙", "标签", "#F8F6F4", "#FFFFFF", "#2A2420", "#88807A", "#EA6A2B", "#FDECE0", "#EBE4DE", CardRadius: 8, BarHeight: 8, Shadow: true, ChipStyle: "tag", Pair: "夜航 · 琥珀", Tags: AmberTags),

        // ---------- 极简：配列表布局最合适，去掉卡片和底色 ----------
        new("极简 · 白", "极简", "#FFFFFF", "#FFFFFF", "#1A1C1E", "#7A8188", "#3D4A56", "#F1F3F5", "#EAECEF", CardRadius: 0, ButtonRadius: 6, BarHeight: 4, ChipStyle: "text", Pair: "极简 · 夜"),
        new("极简 · 夜", "极简", "#1A1C1E", "#1F2225", "#ECEFF1", "#98A0A6", "#7FB4C9", "#2A2E32", "#2F3337", Dark: true, CardRadius: 0, ButtonRadius: 6, BarHeight: 4, ChipStyle: "text", Pair: "极简 · 白"),

        // ---------- 经典：原来的六套，保留原样，老用户升级后视觉不变 ----------
        new("经典 · 竹青", "经典", "#F3F6F1", "#FFFFFF", "#24352F", "#74857A", "#21846B", "#E6F0E9", "#D8E2D8", CardRadius: 10, ButtonRadius: 7, ChipRadius: 6, BarHeight: 20, ChipStyle: "chip", HeaderStyle: "chip", Pair: "经典 · 夜墨"),
        new("经典 · 纸白", "经典", "#F2F2F2", "#FFFFFF", "#2D333B", "#737C88", "#596776", "#ECEFF2", "#DCDDE2", CardRadius: 10, ButtonRadius: 7, ChipRadius: 6, BarHeight: 20, ChipStyle: "chip", HeaderStyle: "chip", Pair: "经典 · 夜墨"),
        new("经典 · 雾蓝", "经典", "#EFF4FA", "#FAFCFF", "#253A55", "#6C819B", "#477FB3", "#E2EDF7", "#CEDCEC", CardRadius: 10, ButtonRadius: 7, ChipRadius: 6, BarHeight: 20, ChipStyle: "chip", HeaderStyle: "chip", Pair: "经典 · 夜墨"),
        new("经典 · 暖杏", "经典", "#FAF3E9", "#FFFCF7", "#544237", "#9A8575", "#B87944", "#F4E8D8", "#EADBC9", CardRadius: 10, ButtonRadius: 7, ChipRadius: 6, BarHeight: 20, ChipStyle: "chip", HeaderStyle: "chip", Pair: "经典 · 夜墨"),
        new("经典 · 夜墨", "经典", "#181E27", "#252D39", "#E9EDF5", "#A6B2C4", "#70BDB0", "#344653", "#404C5C", Dark: true, CardRadius: 10, ButtonRadius: 7, ChipRadius: 6, BarHeight: 20, ChipStyle: "chip", HeaderStyle: "chip", Pair: "经典 · 竹青"),
        new("经典 · 透明", "经典", "#18242D", "#1D2B36", "#FFFFFF", "#D2DEE7", "#91DCC5", "#354C55", "#769291", Dark: true, Glass: true, CardRadius: 10, ButtonRadius: 7, ChipRadius: 6, BarHeight: 20, ChipStyle: "chip", HeaderStyle: "chip", Pair: "经典 · 竹青")
    };

    // 1.5.x 及以前的主题名，升级后映射到对应的经典主题，外观保持不变。
    private static readonly Dictionary<string, string> Legacy = new()
    {
        ["竹青"] = "经典 · 竹青", ["纸白"] = "经典 · 纸白", ["雾蓝"] = "经典 · 雾蓝",
        ["暖杏"] = "经典 · 暖杏", ["夜墨"] = "经典 · 夜墨", ["透明"] = "经典 · 透明"
    };

    public static bool IsKnown(string name) => All.Any(t => t.Name == name) || Legacy.ContainsKey(name);
    public static bool IsHex(string? color) => !string.IsNullOrWhiteSpace(color) && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");
    // 相对亮度与对比度，用来提醒“这个颜色配这个背景看不清”。
    public static double Luminance(string hex)
    {
        if (!IsHex(hex)) return 0;
        int red = Convert.ToInt32(hex.Substring(1, 2), 16), green = Convert.ToInt32(hex.Substring(3, 2), 16), blue = Convert.ToInt32(hex.Substring(5, 2), 16);
        double Channel(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(red) + 0.7152 * Channel(green) + 0.0722 * Channel(blue);
    }
    public static double ContrastRatio(string first, string second)
    {
        if (!IsHex(first) || !IsHex(second)) return 21;
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    public static string Migrate(string stored) => Legacy.TryGetValue(stored, out var mapped) ? mapped : stored;
    public static Theme Find(string name)
    {
        string key = Migrate(name);
        return All.FirstOrDefault(t => t.Name == key) ?? All.First(t => t.Name == Default);
    }
    // 跟随系统深浅色：浅色主题配一个深色搭档，反之亦然。
    public static Theme Follow(Theme theme, bool systemDark)
    {
        if (theme.Pair == "" || theme.Dark == systemDark) return theme;
        return All.FirstOrDefault(t => t.Name == theme.Pair) ?? theme;
    }
    public static IEnumerable<IGrouping<string, Theme>> Grouped() => All.GroupBy(t => t.Family);
}
