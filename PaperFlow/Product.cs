namespace PaperFlow;

// Single place for the product identity so the window, tray, settings page and
// promotional export never drift apart from the assembly version.
public static class Product
{
    public const string Name = "PaperFlow";
    public static string Version => typeof(Product).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
