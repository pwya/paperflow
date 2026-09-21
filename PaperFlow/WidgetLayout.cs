using System;

namespace PaperFlow;

public static class WidgetLayout
{
    public const double MinimumWidth = 360, MinimumHeight = 180;
    public const double MinimalHeight = 120;
    public static string NormalizeMode(string? mode) => mode is "window" or "topmost" ? mode : "desktop";
    public static string NormalizeDisplayMode(string? mode) => mode == "minimal" ? "minimal" : "full";
    public static double OneCardHeight(double chrome, double card, double availableHeight, bool minimal = false)
    {
        double floor = minimal ? MinimalHeight : MinimumHeight;
        return Math.Min(Math.Max(floor, availableHeight), Math.Max(floor, Math.Ceiling(chrome + card + 4)));
    }
}
