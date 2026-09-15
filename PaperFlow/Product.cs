namespace PaperFlow;

// Single place for the product identity so the window, tray, settings page and
// promotional export never drift apart from the assembly version.
public static class Product
{
    public const string Name = "PaperFlow";
    public static string Version => typeof(Product).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    // The release script passes the Git commit as SourceRevisionId, which the SDK appends to
    // the informational version. Local builds simply have no commit to show.
    public static string BuildCommit
    {
        get
        {
            var attribute = (System.Reflection.AssemblyInformationalVersionAttribute?)System.Attribute.GetCustomAttribute(typeof(Product).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            string info = attribute?.InformationalVersion ?? "";
            int plus = info.IndexOf('+');
            if (plus < 0) return "";
            string commit = info[(plus + 1)..].Trim();
            return commit.Length > 7 ? commit[..7] : commit;
        }
    }
}
