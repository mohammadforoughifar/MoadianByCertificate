namespace Inventory.Client.Services;

/// <summary>
/// «نوار پیشرفت» باز کردن نامه.
///
/// <para>
/// چرا لازم است: باز کردن نامهٔ وارده یک درخواست به سرور است و برای نامه‌های
/// سنگین (متنِ چندمگابایتیِ برگرفته از ایمیل) چند ثانیه طول می‌کشد. بدون نشانگر،
/// کاربر فکر می‌کند کلیکش کار نکرده و دوباره کلیک می‌کند.
/// </para>
///
/// <para>
/// رفتار درصد عمداً «تخمینی» است: چون سرور درصد واقعی نمی‌فرستد، عدد تا سقف ۹۲٪
/// به‌آرامی بالا می‌رود (هرچه بیشتر طول بکشد، کندتر) و به‌محضِ رسیدن پاسخ،
/// <see cref="FinishAsync"/> آن را به ۱۰۰٪ می‌برد و بعد پاک می‌کند. هیچ‌وقت
/// قبل از پایان کار به ۱۰۰٪ نمی‌رسد تا کاربر گمان نکند کار تمام شده است.
/// </para>
/// </summary>
public sealed class LoadProgress : IDisposable
{
    /// <summary>سقف تخمینی؛ رسیدن پاسخ واقعی، نوار را به ۱۰۰ می‌برد.</summary>
    private const int Ceiling = 92;

    private readonly Func<Task> _changed;
    private CancellationTokenSource? _cts;

    /// <param name="changed">
    /// بازخورد به کامپوننت برای رندر دوباره — معمولاً
    /// <c>() =&gt; InvokeAsync(StateHasChanged)</c>.
    /// </param>
    public LoadProgress(Func<Task> changed) => _changed = changed;

    /// <summary>درصد جاری (۰ = نوار مخفی).</summary>
    public int Value { get; private set; }

    /// <summary>آیا نوار باید دیده شود؟</summary>
    public bool IsActive => Value > 0;

    /// <summary>شروع/شروع دوبارهٔ پیشرفت.</summary>
    public void Start()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        Value = 7;
        _ = _changed();
        _ = TickAsync(_cts.Token);
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && Value < Ceiling)
            {
                // گام‌ها با بالا رفتن درصد کوچک‌تر می‌شوند تا حس «نزدیک به پایان» نداشته باشد.
                await Task.Delay(110, ct);
                Value += Value < 40 ? 6 : Value < 65 ? 3 : Value < 85 ? 2 : 1;
                if (Value > Ceiling) Value = Ceiling;
                await _changed();
            }
        }
        catch (OperationCanceledException) { /* پایان کار یا شروع دوباره */ }
        catch (Exception) { /* نوار پیشرفت نباید هیچ‌وقت خطا بدهد */ }
    }

    /// <summary>کامل شدن: پرش به ۱۰۰٪، نمایش کوتاه، سپس پاک شدن نوار.</summary>
    public async Task FinishAsync()
    {
        _cts?.Cancel();

        Value = 100;
        try { await _changed(); } catch { /* کامپوننت ممکن است بسته شده باشد */ }

        await Task.Delay(260);

        Value = 0;
        try { await _changed(); } catch { }
    }

    /// <summary>پاک کردن فوری (مثلاً هنگام بستن ریدر).</summary>
    public void Reset()
    {
        _cts?.Cancel();
        Value = 0;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
