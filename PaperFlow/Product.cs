namespace PaperFlow;

// Single place for the product identity so the window, tray, settings page and
// promotional export never drift apart from the assembly version.
// The Git commit of a build is published in build-info.json and in the release assets
// instead of the UI: it is useful to a maintainer and noise to everybody else.
public static class Product
{
    public const string Name = "PaperFlow";
    public static string Version => typeof(Product).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
