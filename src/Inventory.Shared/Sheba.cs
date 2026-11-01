using System.Linq;

namespace Inventory.Shared;

/// <summary>نرمال‌سازی و اعتبارسنجی شبای بانکی ایران (IR + ۲۴ رقم).</summary>
public static class HrSheba
{
    public static string? Norm(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim();
        // ارقام فارسی/عربی به لاتین
        s = s.Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
             .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
             .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
             .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        s = new string(s.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (s.Length == 24 && s.All(char.IsDigit)) s = "IR" + s;
        return s.Length == 0 ? null : s;
    }

    public static bool IsValid(string? normed)
        => normed != null && normed.Length == 26 && normed.StartsWith("IR") && normed[2..].All(char.IsDigit);
}
