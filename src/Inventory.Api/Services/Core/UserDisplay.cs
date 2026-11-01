using Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>نام نمایشی کاربر: نام + نام‌خانوادگی؛ اگر خالی بود نام کاربری.</summary>
public static class UserDisplay
{
    public static string Name(string? firstName, string? lastName, string? username)
    {
        var n = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(n) ? (username ?? "").Trim() : n;
    }

    public static string Name(User? user)
        => user is null ? "" : Name(user.FirstName, user.LastName, user.Username);
}
