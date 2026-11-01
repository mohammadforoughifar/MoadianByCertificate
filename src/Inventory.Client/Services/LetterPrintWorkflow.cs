using System.Diagnostics;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>
/// Shared popup, loading, PDF fallback, and error flow for internal and outgoing letters.
///
/// بودجهٔ زمانی چاپ: قبلاً کل آماده‌سازی (دریافت PDF + تحویل به مرورگر) سقف ثابت ۱۵ ثانیه داشت.
/// ساخت PDF نامه روی سربرگ (QuestPDF + PDFsharp روی تصویر ۲۴۸۱×۳۵۰۸) در سرور کند/بار اول
/// می‌تواند بیشتر طول بکشد؛ نتیجه این بود که صفحهٔ چاپ در حالت «در حال آماده‌سازی» می‌ماند و
/// بعد از ۱۵ ثانیه با خطا بسته می‌شد، در حالی که سرور داشت کار می‌کرد. اکنون:
///   • تلاش اول ۹۰ ثانیه (شامل رندر سرد، JIT و فونت‌ها)
///   • تلاش‌های بعدی ۶۰ ثانیه (کش سرور معمولاً پاسخ را فوری می‌کند)
///   • تحویل به مرورگر بودجهٔ جدا دارد تا زمان دانلود، از سهم مرورگر کم نشود
///   • بعد از ۸ ثانیه متن وضعیت به کاربر می‌گوید کار در جریان است (حس «قفل‌کردن» ندهد)
/// </summary>
public sealed class LetterPrintWorkflow
{
    private static readonly TimeSpan FirstAttemptTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RetryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SlowPreparationHintAfter = TimeSpan.FromSeconds(8);

    private readonly IJSRuntime _js;
    private readonly IToastService _toast;
    private readonly ILogger<LetterPrintWorkflow> _logger;

    public LetterPrintWorkflow(IJSRuntime js, IToastService toast, ILogger<LetterPrintWorkflow> logger)
    {
        _js = js;
        _toast = toast;
        _logger = logger;
    }

    // Keep the original overload for other callers, while print paths that support it
    // pass the cancellation token through the HTTP download as well.
    public Task PrintPdfAsync(Func<Task<byte[]>> loadPdf, string fileName) =>
        PrintPdfAsync(_ => loadPdf(), fileName);

    public async Task PrintPdfAsync(Func<CancellationToken, Task<byte[]>> loadPdf, string fileName)
    {
        var total = Stopwatch.StartNew();
        long lastStageMs = 0;
        void MarkStage(string stage)
        {
            var elapsed = total.ElapsedMilliseconds;
            _logger.LogInformation(
                "LetterPrintClientTiming Stage={Stage} StageMs={StageMs} TotalMs={TotalMs}",
                stage, elapsed - lastStageMs, elapsed);
            lastStageMs = elapsed;
        }

        string? printId;
        try
        {
            printId = await _js.InvokeAsync<string?>("ltxPrintBegin");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create the browser print surface.");
            _toast.Error("پنجرهٔ چاپ باز نشد. پنجره‌های بازشو را برای این سایت مجاز کنید و دوباره تلاش کنید.");
            return;
        }

        if (string.IsNullOrWhiteSpace(printId))
        {
            _toast.Error("صفحهٔ چاپ ساخته نشد. پنجره‌های بازشو را مجاز کنید و دوباره تلاش کنید.");
            return;
        }
        MarkStage("print-surface-ready");

        var attemptNumber = 0;
        while (true)
        {
            attemptNumber++;
            var attempt = Stopwatch.StartNew();
            var loadBudget = attemptNumber == 1 ? FirstAttemptTimeout : RetryTimeout;

            // Hint that the server is still rendering: the first print on a fresh deployment
            // reads the 5 MB template, registers fonts and renders the letterhead image.
            using var hintCancellation = new CancellationTokenSource();
            var slowHint = ShowSlowPreparationHintAsync(printId, hintCancellation.Token);

            try
            {
                // The download and the browser handoff have separate budgets: a slow render must
                // not eat the time the browser needs to build the print preview.
                byte[] pdfBytes;
                using (var loadTimeout = new CancellationTokenSource(loadBudget))
                {
                    pdfBytes = await loadPdf(loadTimeout.Token);
                }
                MarkStage("pdf-download");

                if (pdfBytes == null || pdfBytes.Length == 0)
                    throw new InvalidOperationException("فایل PDF چاپ خالی است.");

                // Blazor transfers byte[] as Uint8Array, avoiding a base64 expansion/copy.
                _logger.LogInformation(
                    "LetterPrintClientTiming Stage=pdf-binary-ready Bytes={Bytes}", pdfBytes.Length);

                bool ready;
                using (var handoffTimeout = new CancellationTokenSource(HandoffTimeout))
                {
                    ready = await _js.InvokeAsync<bool>(
                        "ltxPrintReady", handoffTimeout.Token, printId, pdfBytes, fileName,
                        (int)HandoffTimeout.TotalMilliseconds);
                }
                MarkStage("browser-print-handoff");

                if (ready)
                {
                    MarkStage("complete");
                    return;
                }

                throw new InvalidOperationException("مرورگر نتوانست فایل PDF را برای چاپ آماده کند.");
            }
            catch (Exception ex)
            {
                var cancelledByBudget = ex is OperationCanceledException && attempt.Elapsed >= loadBudget - TimeSpan.FromMilliseconds(500);
                var message = cancelledByBudget
                    ? $"ساخت نسخهٔ چاپ بیش از {(int)loadBudget.TotalSeconds} ثانیه طول کشید. " +
                      "اگر بار اول است، ساخت PDF روی سربرگ کمی کند است؛ «تلاش مجدد» معمولاً خیلی سریع‌تر آماده می‌شود. " +
                      "در صورت تکرار، لاگ چاپ سرور (OutgoingLetterPrintTiming) را ببینید."
                    : ex is OperationCanceledException
                        ? "آماده‌سازی چاپ لغو شد."
                        : ex.Message;

                _logger.LogWarning(ex,
                    "LetterPrintClientTiming Stage=preparation-failed Attempt={Attempt} TotalMs={TotalMs}",
                    attemptNumber, total.ElapsedMilliseconds);
                try { await _js.InvokeVoidAsync("ltxPrintFail", printId, message); }
                catch (Exception jsEx) { _logger.LogWarning(jsEx, "Could not display the print preparation error."); }

                _toast.Error(message);
                string? action;
                try
                {
                    action = await _js.InvokeAsync<string?>("ltxPrintWaitForAction", printId);
                }
                catch (Exception jsEx)
                {
                    _logger.LogWarning(jsEx, "Could not receive a print recovery action.");
                    try { await _js.InvokeVoidAsync("ltxPrintCancel", printId); } catch { }
                    return;
                }

                if (string.Equals(action, "retry", StringComparison.OrdinalIgnoreCase))
                {
                    MarkStage("retry-requested");
                    continue;
                }

                if (string.Equals(action, "direct", StringComparison.OrdinalIgnoreCase))
                {
                    try { await _js.InvokeVoidAsync("ltxPrintDirectPage", printId); }
                    catch (Exception jsEx) { _logger.LogWarning(jsEx, "Direct print fallback failed."); }
                    MarkStage("direct-print-requested");
                    return;
                }

                try { await _js.InvokeVoidAsync("ltxPrintCancel", printId); } catch { }
                MarkStage("print-cancelled");
                return;
            }
            finally
            {
                hintCancellation.Cancel();
                try { await slowHint; } catch { /* status hint is best-effort */ }
            }
        }
    }

    /// <summary>
    /// بعد از چند ثانیه متن وضعیت پنجرهٔ چاپ را به‌روز می‌کند تا کاربر فکر نکند صفحه قفل شده است.
    /// اگر آماده‌سازی سریع تمام شود، توکن لغو می‌شود و هیچ متنی نوشته نمی‌شود.
    /// </summary>
    private async Task ShowSlowPreparationHintAsync(string printId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SlowPreparationHintAfter, cancellationToken);
            await _js.InvokeVoidAsync("ltxPrintStatus", cancellationToken, printId,
                "در حال ساخت PDF روی سربرگ شرکت… (بار اول ممکن است تا یک دقیقه طول بکشد)");
        }
        catch (Exception ex) when (ex is OperationCanceledException or JSDisconnectedException or TaskCanceledException)
        {
            // fast path / navigation: nothing to show
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not update the print status hint.");
        }
    }
}
