using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PaperFlow;

public static class Appearance
{
    public static Theme Current { get; private set; } = Themes.Find(Themes.Default);
    public static string Layout { get; private set; } = Themes.CardLayout;
    public static string ChipStyle { get; private set; } = "text";
    public static int BarHeight { get; private set; } = 6;
    public static bool Dark => Current.Dark;
    public static bool Shadow => Current.Shadow;
    public static string HeaderStyle => Current.HeaderStyle;
    public static double CardRadius => Current.CardRadius * Scale;
    public static double ButtonRadius => Current.ButtonRadius * Scale;
    public static double ChipRadius => Current.ChipRadius * Scale;
    public static string[] Tags => Current.Tags ?? Array.Empty<string>();
    // 界面缩放：间距、圆点、进度条、控件尺寸；字号：文字大小。两者相乘决定文字实际大小。
    public static double Scale { get; private set; } = 1;
    public static double TextScale { get; private set; } = 1;
    public static double EffectiveTextSize { get; private set; } = 13;
    // Dialogs are normal windows: they grow with the widget's text but never past 250%,
    // so the settings form always fits inside its own window.
    public static double DialogScale => Math.Clamp(TextScale, 1, 2.5);
    public static double Opacity { get; private set; } = 1;
    public static string FontName { get; private set; } = "Microsoft YaHei UI";
    public static string AccentText => Luminance(Current.Accent) > 0.5 ? "#182D29" : "#FFFFFF";
    public static string ProgressInk => Dark ? Current.Accent : Mix(Current.Accent, "#18352B", 0.25);
    // 百分比在标题行右侧，字号和颜色跟着风格走：纸感/标签/极简用次要灰，柔光/夜航才用强调色。
    public static double PercentSize => Current.Family switch { "纸感" => 18, "柔光" => 21, "夜航" => 20, "标签" => 18, "极简" => 16, _ => 23 };
    public static bool PercentAccent => Current.Family is "柔光" or "夜航";
    public static bool IsColor(string color) => color.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");
    // Windows 的“应用模式”：浅色 = 1，深色 = 0。读不到就当作浅色。
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception) { return false; }
    }
    private static double Luminance(string color) { var c = (Color)ColorConverter.ConvertFromString(color); return (c.R * .299 + c.G * .587 + c.B * .114) / 255; }
    private static string Mix(string a, string b, double weight)
    {
        var x = (Color)ColorConverter.ConvertFromString(a); var y = (Color)ColorConverter.ConvertFromString(b);
        return $"#{(byte)(x.R*(1-weight)+y.R*weight):X2}{(byte)(x.G*(1-weight)+y.G*weight):X2}{(byte)(x.B*(1-weight)+y.B*weight):X2}";
    }
    public static SolidColorBrush Paint(string color, double alpha = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(color); c.A = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255); return new SolidColorBrush(c);
    }
    public static ImageSource CreateHeaderIcon()
    {
        var drawing = new DrawingGroup();
        using (var canvas = drawing.Open())
        {
            var accent = Paint(Current.Accent); var ink = Paint(AccentText);
            canvas.DrawRoundedRectangle(accent, null, new Rect(0, 0, 26, 26), 6, 6);
            canvas.DrawGeometry(ink, null, Geometry.Parse("M7,5 L16,5 L20,9 L20,21 L7,21 Z"));
            var line = new Pen(accent, 1.3);
            canvas.DrawLine(line, new Point(10, 9), new Point(15, 9));
            canvas.DrawLine(line, new Point(10, 12), new Point(17, 12));
            canvas.DrawGeometry(null, new Pen(accent, 1.8), Geometry.Parse("M10,16 L12,18 L17,14"));
        }
        drawing.Freeze(); return new DrawingImage(drawing);
    }
    public static void Apply(Preferences p)
    {
        Current = Themes.Find(p.Theme);
        if (p.FollowSystemTheme) Current = Themes.Follow(Current, SystemIsDark());
        if (IsColor(p.AccentColor) && p.AccentColor != "") Current = Current with { Accent = p.AccentColor };
        if (IsColor(p.BackgroundColor) && p.BackgroundColor != "") Current = Current with { Window = p.BackgroundColor, Card = Mix(p.BackgroundColor, Current.Ink, .06) };
        Scale = p.UiScale;
        TextScale = p.TextSize / 13 * p.UiScale;
        EffectiveTextSize = 13 * TextScale;
        Layout = Themes.Layouts.Contains(p.ListLayout) ? p.ListLayout : Themes.CardLayout;
        BarHeight = p.BarHeight > 0 ? p.BarHeight : Current.BarHeight;
        // The list layout drops the chip blocks regardless of the theme, otherwise rows get too tall.
        ChipStyle = Layout == Themes.ListLayout ? "text" : Current.ChipStyle;
        Opacity = p.BackgroundOpacity; FontName = p.FontName;
        var resources = Application.Current.Resources;
        foreach (var pair in new Dictionary<string, string> { ["Ink"] = Current.Ink, ["Muted"] = Current.Muted, ["Accent"] = Current.Accent, ["AccentText"] = AccentText, ["Soft"] = Current.Soft, ["Card"] = Current.Card, ["Line"] = Current.Border, ["WindowBackground"] = Current.Window }) resources[pair.Key] = Paint(pair.Value);
        resources["ButtonCorner"] = new CornerRadius(ButtonRadius);
        resources["InputCorner"] = new CornerRadius(ButtonRadius);
    }
    public static SolidColorBrush Map(string original) => Paint(original switch
    {
        "#24352F" => Current.Ink,
        "#78867F" or "#62766A" or "#8A948C" or "#6C7C70" or "#99A49B" or "#567062" => Current.Muted,
        "#21846B" or "#2F8B6D" or "#4A9E83" => Current.Accent,
        "#F5F6F2" => Current.Window,
        "#CBD6CD" or "#E0E5DD" => Current.Border,
        "#EBEFE9" or "#F0F2EE" or "#E6F3EC" => Current.Soft,
        "#FFF0DB" => Current.Name is "夜墨" or "透明" ? "#62513A" : "#FFF0DB",
        "#9D6925" => Current.Name is "夜墨" or "透明" ? "#F2CD8B" : "#9D6925",
        _ => original
    });
}
