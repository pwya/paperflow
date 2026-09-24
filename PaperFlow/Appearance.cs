using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    public static string ImagePath { get; private set; } = "";
    public static double Scrim { get; private set; } = 0.35;
    public static string FontName { get; private set; } = "Microsoft YaHei UI";
    private static string titleFont = "", bodyFont = "", captionFont = "";
    private static double titleScale = 1, bodyScale = 1, captionScale = 1;
    private static string titleColor = "", bodyColor = "", captionColor = "";
    public static string FamilyFor(string role) => role switch { "title" => titleFont, "caption" => captionFont, _ => bodyFont };
    public static double RoleScale(string role) => role switch { "title" => titleScale, "caption" => captionScale, _ => bodyScale };
    public static string RoleColor(string role) => role switch { "title" => titleColor, "caption" => captionColor, _ => bodyColor };
    private static readonly Dictionary<string, BitmapImage> ImageCache = new();
    // 背景图片：按最长边 1600 解码，避免超大图吃内存；同一路径只读一次。
    public static ImageSource? BackgroundImage()
    {
        if (ImagePath == "" || !File.Exists(ImagePath)) return null;
        string key = ImagePath + "|" + File.GetLastWriteTimeUtc(ImagePath).Ticks;
        if (ImageCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(ImagePath);
            bitmap.DecodePixelWidth = 1600;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            if (ImageCache.Count > 8) ImageCache.Clear();
            ImageCache[key] = bitmap;
            return bitmap;
        }
        catch (Exception) { return null; }
    }
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
        // 语言先定下来：后面的文案、主题名都要按它取。
        Lang.Apply(p.Language);
        Current = Themes.Find(p.Theme);
        if (p.FollowSystemTheme) Current = Themes.Follow(Current, SystemIsDark());
        if (IsColor(p.AccentColor) && p.AccentColor != "") Current = Current with { Accent = p.AccentColor };
        if (IsColor(p.BackgroundColor) && p.BackgroundColor != "") Current = Current with { Window = p.BackgroundColor, Card = Mix(p.BackgroundColor, Current.Ink, .06) };
        Scale = p.UiScale;
        TextScale = p.TextSize / 13 * p.UiScale;
        EffectiveTextSize = 13 * TextScale;
        Layout = Themes.Layouts.Contains(p.ListLayout) ? p.ListLayout : Themes.CardLayout;
        BarHeight = p.BarHeight > 0 ? p.BarHeight : Current.BarHeight;
        ImagePath = p.BackgroundImage ?? ""; Scrim = p.ImageScrim;
        // The list layout drops the chip blocks regardless of the theme, otherwise rows get too tall.
        ChipStyle = Layout == Themes.ListLayout ? "text" : Current.ChipStyle;
        Opacity = p.BackgroundOpacity; FontName = p.FontName;
        string baseFont = string.IsNullOrWhiteSpace(p.FontName) ? "Microsoft YaHei UI" : p.FontName;
        titleFont = string.IsNullOrWhiteSpace(p.TitleFont) ? baseFont : p.TitleFont;
        bodyFont = string.IsNullOrWhiteSpace(p.BodyFont) ? baseFont : p.BodyFont;
        captionFont = string.IsNullOrWhiteSpace(p.CaptionFont) ? baseFont : p.CaptionFont;
        titleScale = p.TitleScale; bodyScale = p.BodyScale; captionScale = p.CaptionScale;
        titleColor = Themes.IsHex(p.TitleColor) ? p.TitleColor : "";
        bodyColor = Themes.IsHex(p.BodyColor) ? p.BodyColor : "";
        captionColor = Themes.IsHex(p.CaptionColor) ? p.CaptionColor : "";
        var resources = Application.Current.Resources;
        foreach (var pair in new Dictionary<string, string> { ["Ink"] = Current.Ink, ["Muted"] = Current.Muted, ["Accent"] = Current.Accent, ["AccentText"] = AccentText, ["Soft"] = Current.Soft, ["Card"] = Current.Card, ["Line"] = Current.Border, ["WindowBackground"] = Current.Window }) resources[pair.Key] = Paint(pair.Value);
        resources["ButtonCorner"] = new CornerRadius(ButtonRadius);
        resources["InputCorner"] = new CornerRadius(ButtonRadius);
        resources["PopupFontSize"] = 13 * DialogScale;
        resources["PopupFontFamily"] = new FontFamily(baseFont);
        resources["EditUndo"] = Lang.T("撤销"); resources["EditCut"] = Lang.T("剪切");
        resources["EditCopy"] = Lang.T("复制"); resources["EditPaste"] = Lang.T("粘贴"); resources["EditSelectAll"] = Lang.T("全选");
        foreach (var role in new[] { "title", "body", "caption" })
        {
            var custom = RoleColor(role);
            resources[role + "Ink"] = Paint(custom == "" ? Current.Ink : custom);
            resources[role + "Muted"] = Paint(custom == "" ? Current.Muted : custom);
            resources[role + "Accent"] = Paint(custom == "" ? Current.Accent : custom);
        }
    }
    public static SolidColorBrush Map(string original) => Paint(original switch
    {
        "#24352F" => Current.Ink,
        "#78867F" or "#62766A" or "#8A948C" or "#6C7C70" or "#99A49B" or "#567062" => Current.Muted,
        "#21846B" or "#2F8B6D" or "#4A9E83" => Current.Accent,
        "#F5F6F2" => Current.Window,
        "#CBD6CD" or "#E0E5DD" => Current.Border,
        "#EBEFE9" or "#F0F2EE" or "#E6F3EC" => Current.Soft,
        _ => original
    });
}
