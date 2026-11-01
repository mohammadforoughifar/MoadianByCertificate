namespace Inventory.Client.Services;

public class ToastMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Message { get; init; } = "";
    public string Kind { get; init; } = "info"; // success | error | info
}

/// <summary>نمایش اعلان‌های کوتاه (Toast).</summary>
public class ToastService : IToastService
{
    private readonly List<ToastMessage> _toasts = new();
    public IReadOnlyList<ToastMessage> Toasts => _toasts;
    public event Action? OnChange;

    public void Success(string message) => Show(message, "success");
    public void Error(string message) => Show(message, "error");
    public void Warning(string message) => Show(message, "warning");
    public void Info(string message) => Show(message, "info");

    public void Show(string message, string kind)
    {
        var toast = new ToastMessage { Message = message, Kind = kind };
        _toasts.Add(toast);
        // با مدت نمایش طولانی، چند پیام پشت‌سرهم باید هم‌زمان دیده شوند (قبلاً ۴ بود)
        if (_toasts.Count > 6) _toasts.RemoveAt(0);
        OnChange?.Invoke();
        _ = AutoRemove(toast.Id);
    }

    public void Remove(Guid id)
    {
        var t = _toasts.FirstOrDefault(x => x.Id == id);
        if (t is null) return;
        _toasts.Remove(t);
        OnChange?.Invoke();
    }

    private async Task AutoRemove(Guid id)
    {
        // مدت نمایش از تنظیمات کاربر (پنل اعلان‌ها) می‌آید؛ صفر یعنی تا بستن دستی بماند.
        var delay = NotificationTiming.ToastMs;
        if (delay <= 0) return;
        await Task.Delay(delay);
        Remove(id);
    }
}

/// <summary>عنوان صفحه جاری برای نوار بالا.</summary>
public class LayoutState
{
    private string _title = "داشبورد";
    public string Title => _title;
    public event Action? Changed;

    public void SetTitle(string title)
    {
        if (_title == title) return;
        _title = title;
        Changed?.Invoke();
    }
}
