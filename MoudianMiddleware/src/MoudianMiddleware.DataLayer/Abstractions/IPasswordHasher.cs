namespace MoudianMiddleware.DataLayer.Abstractions;

/// <summary>
/// هش‌کردن و بررسی رمز عبور کاربران.
/// </summary>
/// <remarks>
/// این قرارداد در DataLayer تعریف شده است چون Seeder اولیه (ایجاد کاربر مدیر) به آن نیاز دارد؛
/// پیاده‌سازی (PBKDF2) در CoreLayer است و از طریق DI تزریق می‌شود.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>ساخت هش قابل ذخیره برای رمز عبور.</summary>
    /// <param name="password">رمز عبور خام.</param>
    /// <returns>رشته‌ی هش‌شده (شامل الگوریتم/نمک/تکرار).</returns>
    string Hash(string password);

    /// <summary>بررسی تطابق رمز عبور با هش ذخیره‌شده.</summary>
    /// <param name="password">رمز عبور خام.</param>
    /// <param name="passwordHash">هش ذخیره‌شده.</param>
    /// <returns>در صورت تطابق <c>true</c>.</returns>
    bool Verify(string password, string passwordHash);
}
