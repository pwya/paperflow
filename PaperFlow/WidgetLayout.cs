using System;

namespace PaperFlow;

public static class WidgetLayout
{
    public const double MinimumWidth = 360, MinimumHeight = 180;
    public static string NormalizeMode(string? mode) => mode is "window" or "topmost" ? mode : "desktop";
    public static double OneCardHeight(double chrome, double card, double availableHeight) =>
        Math.Min(Math.Max(MinimumHeight, availableHeight), Math.Max(MinimumHeight, Math.Ceiling(chrome + card + 4)));
}
