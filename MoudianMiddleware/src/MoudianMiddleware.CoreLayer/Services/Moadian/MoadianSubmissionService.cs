using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Common.Exceptions;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;

namespace MoudianMiddleware.CoreLayer.Services.Moadian;

/// <summary>ارسال و استعلام صورتحساب؛ فقط metadata و خطاهای عمومی در تاریخچه ذخیره می‌شوند.</summary>
public sealed class MoadianSubmissionService : IMoadianSubmissionService
{
    private const string SafeFailureMessage = "ارسال به سامانه‌ی مودیان ناموفق بود؛ جزئیات خام پاسخ ذخیره نشده است.";
    private readonly MoudianDbContext _context;
    private readonly IMoadianGateway _gateway;

    /// <summary>ساخت سرویس با DbContext و مرز SDK.</summary>
    public MoadianSubmissionService(MoudianDbContext context, IMoadianGateway gateway)
    {
        _context = context;
        _gateway = gateway;
    }

    /// <inheritdoc />
    public async Task<MoadianSendReceipt> SubmitAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.Invoices
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Invoice), invoiceId);

        if (invoice.Status != InvoiceStatus.ReadyToSend)
        {
            throw new InvalidOperationException("فقط صورتحساب ReadyToSend قابل ارسال است؛ ارسال مجدد خودکار انجام نمی‌شود.");
        }
        if (await _context.InvoiceSubmissions.AnyAsync(
                x => x.InvoiceId == invoiceId && x.Status == SubmissionStatus.Pending,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("برای این صورتحساب تلاش حل‌نشده وجود دارد؛ پیش از ارسال مجدد ابتدا استعلام کنید.");
        }

        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianSetting), invoice.TenantId);
        var credential = await _context.MoadianCredentials.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianCredential), invoice.TenantId);

        cancellationToken.ThrowIfCancellationRequested();
        var claimed = await _context.Invoices
            .Where(x => x.Id == invoiceId && x.Status == InvoiceStatus.ReadyToSend)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, InvoiceStatus.Sending), cancellationToken)
            .ConfigureAwait(false);
        if (claimed != 1)
        {
            throw new BusinessConflictException("صورتحساب هم‌زمان توسط درخواست دیگری برای ارسال برداشته شده است.");
        }
        invoice.Status = InvoiceStatus.Sending;

        var now = DateTimeOffset.UtcNow;
        var submission = new InvoiceSubmission
        {
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            AttemptNumber = (await _context.InvoiceSubmissions
                .Where(x => x.InvoiceId == invoice.Id)
                .Select(x => (int?)x.AttemptNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0) + 1,
            Action = SubmissionAction.Send,
            Status = SubmissionStatus.Pending,
            StartedAt = now
        };
        invoice.Status = InvoiceStatus.Sending;
        invoice.Submissions.Add(submission);
        try
        {
            // Commit the claim/submission before network I/O; caller cancellation must not split these state changes.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await _context.Invoices
                    .Where(x => x.Id == invoiceId && x.Status == InvoiceStatus.Sending)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, InvoiceStatus.ReadyToSend), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original persistence exception; a remaining Sending status is visible for operator repair.
            }
            throw;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var receipt = await _gateway.SendInvoiceAsync(setting, credential, invoice, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            invoice.Uid = receipt.Uid;
            invoice.ReferenceNumber = receipt.ReferenceNumber;
            invoice.SentAt = completedAt;
            invoice.Status = InvoiceStatus.Sent;
            submission.Uid = receipt.Uid;
            submission.ReferenceNumber = receipt.ReferenceNumber;
            submission.Status = SubmissionStatus.Succeeded;
            submission.CompletedAt = completedAt;
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            // Once the remote side has accepted the packet, persist the receipt even if the caller disconnected.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return receipt;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            submission.Status = SubmissionStatus.Pending;
            submission.ErrorCode = "OUTCOME_UNKNOWN";
            submission.ErrorMessage = "درخواست لغو یا متوقف شد؛ نتیجه‌ی سمت سامانه نامشخص است و ارسال مجدد خودکار ممنوع است.";
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (MoadianGatewayException exception)
        {
            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;
            invoice.Status = InvoiceStatus.Failed;
            invoice.LastErrorCode = exception.SdkExceptionType;
            invoice.LastErrorMessage = SafeFailureMessage;
            submission.Status = SubmissionStatus.Failed;
            submission.ErrorCode = exception.SdkExceptionType;
            submission.ErrorMessage = SafeFailureMessage;
            submission.CompletedAt = completedAt;
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            // Do not let a canceled request prevent recording the safe failure metadata.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MoadianInquiryResult>> InquiryAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.Invoices.SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        if (string.IsNullOrWhiteSpace(invoice.ReferenceNumber))
        {
            throw new InvalidOperationException("برای این صورتحساب شماره‌ی پیگیری موجود نیست.");
        }

        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianSetting), invoice.TenantId);
        var credential = await _context.MoadianCredentials.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianCredential), invoice.TenantId);
        var submission = new InvoiceSubmission
        {
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            AttemptNumber = (await _context.InvoiceSubmissions
                .Where(x => x.InvoiceId == invoice.Id)
                .Select(x => (int?)x.AttemptNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0) + 1,
            Action = SubmissionAction.Inquiry,
            Status = SubmissionStatus.Pending,
            ReferenceNumber = invoice.ReferenceNumber,
            StartedAt = DateTimeOffset.UtcNow
        };
        await _context.InvoiceSubmissions.AddAsync(submission, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var results = await _gateway.InquiryByReferenceNumbersAsync(
                setting,
                credential,
                new[] { invoice.ReferenceNumber },
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var result = results.FirstOrDefault();
            var completedAt = DateTimeOffset.UtcNow;
            submission.CompletedAt = completedAt;
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            if (result is null)
            {
                submission.Status = SubmissionStatus.NotFound;
                submission.MoadianStatus = "NOT_FOUND";
            }
            else
            {
                submission.Uid = result.Uid;
                submission.ReferenceNumber = result.ReferenceNumber;
                submission.MoadianStatus = result.Status;
                ApplyInquiryStatus(invoice, submission, result.Status);
            }

            // Do not discard a completed inquiry outcome if the HTTP caller canceled while the SDK call was in flight.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return results;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            submission.Status = SubmissionStatus.Pending;
            submission.ErrorCode = "INQUIRY_INTERRUPTED";
            submission.ErrorMessage = "استعلام لغو یا متوقف شد؛ برای نتیجه‌ی به‌روز دوباره استعلام کنید.";
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (MoadianGatewayException exception)
        {
            stopwatch.Stop();
            submission.Status = SubmissionStatus.Failed;
            submission.ErrorCode = exception.SdkExceptionType;
            submission.ErrorMessage = SafeFailureMessage;
            submission.CompletedAt = DateTimeOffset.UtcNow;
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MoadianInvoiceStatusResult>> InquiryByTaxIdAsync(
        int invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _context.Invoices.SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        if (string.IsNullOrWhiteSpace(invoice.TaxId))
        {
            throw new InvalidOperationException("برای این صورتحساب شناسه‌ی TaxId تولید نشده است؛ استعلام ممکن نیست.");
        }

        var setting = await _context.MoadianSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianSetting), invoice.TenantId);
        var credential = await _context.MoadianCredentials.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MoadianCredential), invoice.TenantId);
        var submission = new InvoiceSubmission
        {
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            AttemptNumber = (await _context.InvoiceSubmissions
                .Where(x => x.InvoiceId == invoice.Id)
                .Select(x => (int?)x.AttemptNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0) + 1,
            Action = SubmissionAction.Inquiry,
            Status = SubmissionStatus.Pending,
            StartedAt = DateTimeOffset.UtcNow
        };
        await _context.InvoiceSubmissions.AddAsync(submission, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var results = await _gateway.InquiryByTaxIdsAsync(
                setting,
                credential,
                new[] { invoice.TaxId },
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var result = results.FirstOrDefault(x => string.Equals(x.TaxId, invoice.TaxId, StringComparison.Ordinal));
            var completedAt = DateTimeOffset.UtcNow;
            submission.CompletedAt = completedAt;
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            if (result is null)
            {
                submission.Status = SubmissionStatus.NotFound;
                submission.MoadianStatus = "NOT_FOUND";
            }
            else
            {
                submission.MoadianStatus = result.InvoiceStatus;
                ApplyInquiryStatus(invoice, submission, result.InvoiceStatus);
            }

            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return results;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            submission.Status = SubmissionStatus.Pending;
            submission.ErrorCode = "INQUIRY_INTERRUPTED";
            submission.ErrorMessage = "استعلام لغو یا متوقف شد؛ برای نتیجه‌ی به‌روز دوباره استعلام کنید.";
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (MoadianGatewayException exception)
        {
            stopwatch.Stop();
            submission.Status = SubmissionStatus.Failed;
            submission.ErrorCode = exception.SdkExceptionType;
            submission.ErrorMessage = SafeFailureMessage;
            submission.CompletedAt = DateTimeOffset.UtcNow;
            submission.DurationMs = ToDurationMilliseconds(stopwatch.ElapsedMilliseconds);
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static void ApplyInquiryStatus(Invoice invoice, InvoiceSubmission submission, string status)
    {
        switch (status.ToUpperInvariant())
        {
            case "SUCCESS":
                invoice.Status = InvoiceStatus.Verified;
                invoice.VerifiedAt = DateTimeOffset.UtcNow;
                submission.Status = SubmissionStatus.Succeeded;
                break;
            case "FAILED":
                invoice.Status = InvoiceStatus.Rejected;
                submission.Status = SubmissionStatus.Failed;
                break;
            case "TIMEOUT":
                submission.Status = SubmissionStatus.Timeout;
                break;
            case "IN_PROGRESS":
                invoice.Status = InvoiceStatus.InProgress;
                submission.Status = SubmissionStatus.Pending;
                break;
            case "NOT_FOUND":
                submission.Status = SubmissionStatus.NotFound;
                break;
            default:
                // Unknown future SDK/server statuses normalize to UNKNOWN and do not alter invoice state.
                submission.Status = SubmissionStatus.Pending;
                break;
        }
    }

    private static int ToDurationMilliseconds(long milliseconds) =>
        milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
}
