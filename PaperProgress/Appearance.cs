using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PaperProgress;

public static class Appearance
{
    public sealed record Palette(string Name, string Window, string Card, string Ink, string Muted, string Accent, string Soft, string Border, bool Glass = false);
    public static readonly Palette[] Presets = {
        new("竹青", "#F3F6F1", "#FFFFFF", "#24392F", "#74857A", "#21846B", "#E6F0E9", "#D8E2D8"),
        new("纸白", "#F2F2F2", "#FFFFFF", "#2D333B", "#737C88", "#596776", "#ECEFF2", "#DCDDE2"),
        new("雾蓝", "#EFF4FA", "#FAFCFF", "#253A55", "#6C819B", "#477FB3", "#E2EDF7", "#CEDCEC"),
        new("暖杏", "#FAF3E9", "#FFFCF7", "#544237", "#9A8575", "#B87944", "#F4E8D8", "#EADBC9"),
        new("夜墨", "#181E27", "#252D39", "#E9EDF5", "#A6B2C4", "#70BDB0", "#344653", "#404C5C"),
        new("透明", "#18242D", "#1D2B36", "#FFFFFF", "#D2DEE7", "#91DCC5", "#354C55", "#769291", true)
    };
    public static Palette Current { get; private set; } = Presets[0];
    public static double Scale { get; private set; } = 1;
    public static double Opacity { get; private set; } = 1;
    public static string FontName { get; private set; } = "Microsoft YaHei UI";
    public static string AccentText => Luminance(Current.Accent) > 0.5 ? "#182D29" : "#FFFFFF";
    public static string ProgressInk => Current.Name is "夜墨" or "透明" ? Current.Accent : Mix(Current.Accent, "#18352B", 0.25);
    public static bool IsColor(string color) => color.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");
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
    public static void Apply(Preferences p)
    {
        Current = Presets.FirstOrDefault(x => x.Name == p.Theme) ?? Presets[0];
        if (IsColor(p.AccentColor) && p.AccentColor != "") Current = Current with { Accent = p.AccentColor };
        if (IsColor(p.BackgroundColor) && p.BackgroundColor != "") Current = Current with { Window = p.BackgroundColor, Card = Mix(p.BackgroundColor, Current.Ink, .06) };
        Scale = p.TextSize / 13; Opacity = p.BackgroundOpacity; FontName = p.FontName;
        var resources = Application.Current.Resources;
        foreach (var pair in new Dictionary<string, string> { ["Ink"] = Current.Ink, ["Muted"] = Current.Muted, ["Accent"] = Current.Accent, ["AccentText"] = AccentText, ["Soft"] = Current.Soft, ["Card"] = Current.Card, ["Line"] = Current.Border, ["WindowBackground"] = Current.Window }) resources[pair.Key] = Paint(pair.Value);
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
