using System;
using System.IO;

namespace PaperFlow;

public sealed class PaperResource
{
    public string Kind { get; set; } = "web";
    public string Target { get; set; } = "";
}

public static class PaperResources
{
    public static bool IsWeb(string target, out Uri? uri) =>
        Uri.TryCreate(target, UriKind.Absolute, out uri) &&
        uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host);

    // Called on a changed editor input only. An unchanged missing folder stays saved.
    public static bool TryResolve(string input, out PaperResource? resource)
    {
        resource = null;
        string target = input.Trim();
        if (target.Length == 0) return true;
        if (IsWeb(target, out var uri))
        {
            resource = new PaperResource { Kind = "web", Target = uri!.AbsoluteUri };
            return true;
        }
        try
        {
            if (!Path.IsPathFullyQualified(target) || !Directory.Exists(target)) return false;
            resource = new PaperResource { Kind = "folder", Target = Path.GetFullPath(target) };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }
    }

    // Revalidate at launch: a saved target may have moved, or come from an older file.
    public static string? LaunchTarget(PaperResource resource)
    {
        if (resource.Kind == "web" && IsWeb(resource.Target, out var uri)) return uri!.AbsoluteUri;
        if (resource.Kind == "folder" && Path.IsPathFullyQualified(resource.Target) && Directory.Exists(resource.Target))
            return Path.GetFullPath(resource.Target);
        return null;
    }
}
