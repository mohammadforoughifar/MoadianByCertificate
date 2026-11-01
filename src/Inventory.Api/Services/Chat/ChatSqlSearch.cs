using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Inventory.Api.Services;

/// <summary>
/// سازندهٔ Expression برای جست‌وجوی نرمال‌شدهٔ متن در سطح دیتابیس.
/// معادل متنی <see cref="Inventory.Shared.ChatSearchText.Normalize"/> را با زنجیرهٔ
/// REPLACE/LOWER می‌سازد که EF Core آن را به SQL ترجمه می‌کند؛ بنابراین فیلتر، شمارش و
/// صفحه‌بندی جست‌وجو همگی در دیتابیس انجام می‌شوند، نه روی فهرست materialize شده.
/// تفاوت‌های نادر حالت‌های ترکیبی Unicode (FormKC) در این معادل SQL پوشش داده نمی‌شود.
/// این فایل بدون وابستگی به DbContext است تا در تست‌های انتقال SQL هم قابل لینک باشد.
/// </summary>
public static class ChatSqlSearch
{
    private static readonly MethodInfo ReplaceMethod =
        typeof(string).GetMethod(nameof(string.Replace), new[] { typeof(string), typeof(string) })!;
    private static readonly MethodInfo ToLowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo ContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

    /// <summary>
    /// نگاشت‌های معادل Normalize: ي/ى→ی، ك→ک، ارقام فارسی/عربی→لاتین،
    /// حذف فاصله/نیم‌فاصله/کاراکترهای کنترلی/اعراب/کشیده، و در پایان lowercase.
    /// </summary>
    private static readonly (string From, string To)[] Map =
    {
        ("\u064A", "\u06CC"), ("\u0649", "\u06CC"), ("\u0643", "\u06A9"),
        ("\u06F0", "0"), ("\u06F1", "1"), ("\u06F2", "2"), ("\u06F3", "3"), ("\u06F4", "4"),
        ("\u06F5", "5"), ("\u06F6", "6"), ("\u06F7", "7"), ("\u06F8", "8"), ("\u06F9", "9"),
        ("\u0660", "0"), ("\u0661", "1"), ("\u0662", "2"), ("\u0663", "3"), ("\u0664", "4"),
        ("\u0665", "5"), ("\u0666", "6"), ("\u0667", "7"), ("\u0668", "8"), ("\u0669", "9"),
        (" ", ""), ("\t", ""), ("\n", ""), ("\r", ""), ("\u00A0", ""),
        ("\u200C", ""), ("\u200D", ""), ("\u200E", ""), ("\u200F", ""), ("\u202F", ""),
        ("\uFEFF", ""), ("\u0640", ""),
        ("\u064B", ""), ("\u064C", ""), ("\u064D", ""), ("\u064E", ""), ("\u064F", ""), ("\u0650", ""),
        ("\u0651", ""), ("\u0652", ""), ("\u0653", ""), ("\u0655", ""), ("\u0656", ""), ("\u0670", ""),
    };

    /// <summary>زنجیرهٔ نرمال‌سازی قابل ترجمه به SQL (null به رشتهٔ خالی تبدیل می‌شود).</summary>
    public static Expression Normalize(Expression text)
    {
        Expression e = Expression.Coalesce(text, Expression.Constant(string.Empty));
        foreach (var (from, to) in Map)
            e = Expression.Call(e, ReplaceMethod, Expression.Constant(from), Expression.Constant(to));
        return Expression.Call(e, ToLowerMethod);
    }

    /// <summary>گزارهٔ «نرمال‌شدهٔ فیلد شامل عبارت نرمال‌شدهٔ جست‌وجوست».</summary>
    public static Expression<Func<T, bool>> FieldMatches<T>(Expression<Func<T, string?>> field, string normalizedSearch)
    {
        var body = Expression.Call(Normalize(field.Body), ContainsMethod, Expression.Constant(normalizedSearch));
        return Expression.Lambda<Func<T, bool>>(body, field.Parameters[0]);
    }

    /// <summary>گزارهٔ «Any روی کالکشن با گزارهٔ عضو».</summary>
    public static Expression<Func<T, bool>> AnyMatches<T, TItem>(
        Expression<Func<T, IEnumerable<TItem>>> collection,
        Expression<Func<TItem, bool>> itemPredicate)
    {
        var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), new[] { typeof(TItem) },
            collection.Body, itemPredicate);
        return Expression.Lambda<Func<T, bool>>(any, collection.Parameters[0]);
    }

    /// <summary>یا کردن دو گزاره روی همان پارامتر.</summary>
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Combine(left, right, Expression.OrElse);

    /// <summary>و کردن دو گزاره روی همان پارامتر.</summary>
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Combine(left, right, Expression.AndAlso);

    private static Expression<Func<T, bool>> Combine<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> merge)
    {
        var body = merge(left.Body, new ParameterSwap(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!);
        return Expression.Lambda<Func<T, bool>>(body, left.Parameters[0]);
    }

    private sealed class ParameterSwap : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;
        public ParameterSwap(ParameterExpression from, ParameterExpression to) => (_from, _to) = (from, to);
        protected override Expression VisitParameter(ParameterExpression node)
            => node == _from ? _to : base.VisitParameter(node);
    }
}
