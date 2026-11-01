namespace MoudianMiddleware.CoreLayer.Common.Exceptions;

/// <summary>نشان می‌دهد رکورد در محدوده‌ی مودی جاری وجود ندارد.</summary>
public sealed class EntityNotFoundException : Exception
{
    /// <summary>ساخت خطای رکورد پیدا نشد.</summary>
    public EntityNotFoundException(string entityName, int id)
        : base($"{entityName} با شناسه‌ی {id} پیدا نشد.")
    {
        EntityName = entityName;
        EntityId = id;
    }

    /// <summary>نام نوع رکورد.</summary>
    public string EntityName { get; }
    /// <summary>شناسه‌ی رکورد.</summary>
    public int EntityId { get; }
}
