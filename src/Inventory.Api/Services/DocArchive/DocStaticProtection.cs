namespace Inventory.Api.Services.DocArchive;
/// <summary>Archive bytes may only be served by authenticated endpoints, never the public web root.</summary>
public static class DocStaticProtection
{
    public static bool IsProtectedPath(string path)
    {
        try { path = Uri.UnescapeDataString(Uri.UnescapeDataString(path)).Replace('\\', '/'); }
        catch (UriFormatException) { return true; }
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return parts.Count >= 2 && parts[0].Equals("uploads", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("DocVersion", StringComparison.OrdinalIgnoreCase);
    }
}
