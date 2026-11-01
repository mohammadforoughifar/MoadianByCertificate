namespace Inventory.Shared.Files;

/// <summary>One filename/MIME policy for the server and every eye/preview button.</summary>
public static class AttachmentPreviewFormats
{
    public static string Kind(string? fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        // Prefer a recognized suffix: old uploads often store an incorrect generic MIME.
        if (ext == ".pdf") return "pdf";
        if (ext is ".png" or ".jpg" or ".jpeg" or ".jfif" or ".gif" or ".webp" or ".bmp"
            or ".svg" or ".tif" or ".tiff" or ".ico" or ".avif" or ".heic" or ".heif") return "image";
        if (ext is ".docx" or ".docm" or ".dotx" or ".dotm" or ".xlsx" or ".xlsm" or ".xltx" or ".xltm"
            or ".pptx" or ".pptm" or ".ppsx" or ".ppsm" or ".potx" or ".potm" or ".csv"
            or ".doc" or ".xls" or ".ppt" or ".pps") return "office";
        if (ext is ".txt" or ".json" or ".xml" or ".md" or ".log" or ".html" or ".htm") return "text";
        if (mime == "application/pdf") return "pdf";
        if (mime.StartsWith("image/")) return "image";
        if (mime.Contains("wordprocessingml") || mime.Contains("spreadsheetml") || mime.Contains("presentationml")
            || mime.Contains("msword") || mime.Contains("ms-excel") || mime.Contains("ms-powerpoint")
            || mime.Contains("macroenabled") || mime.Contains("csv")) return "office";
        if (mime.StartsWith("text/") || mime.Contains("json") || mime.Contains("xml")) return "text";
        return "other";
    }

    public static string Mime(string? fileName, string? stored)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var known = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png", ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
            ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml", ".tif" or ".tiff" => "image/tiff",
            ".ico" => "image/x-icon", ".avif" => "image/avif", ".heic" or ".heif" => "image/heic",
            ".docx" or ".docm" or ".dotx" or ".dotm" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" or ".xlsm" or ".xltx" or ".xltm" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" or ".pptm" or ".ppsx" or ".ppsm" or ".potx" or ".potm" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".doc" => "application/msword", ".xls" => "application/vnd.ms-excel", ".ppt" or ".pps" => "application/vnd.ms-powerpoint",
            ".csv" => "text/csv", ".txt" or ".log" or ".md" or ".html" or ".htm" => "text/plain",
            ".json" => "application/json", ".xml" => "application/xml", _ => null
        };
        if (known != null) return known;
        var mime = (stored ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return mime is "image/jpg" or "image/pjpeg" ? "image/jpeg"
            : string.IsNullOrWhiteSpace(mime) ? "application/octet-stream" : mime;
    }
}
