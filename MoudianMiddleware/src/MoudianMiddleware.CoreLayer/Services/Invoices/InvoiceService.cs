using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.CoreLayer.Common.Exceptions;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Abstractions;
using MoudianMiddleware.DataLayer.Abstractions.Queries;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.UnitOfWork;

namespace MoudianMiddleware.CoreLayer.Services.Invoices;

/// <summary>ثبت و خواندن صورتحساب در tenant جاری؛ ExternalId با ایندکس پایگاه‌داده idempotent می‌شود.</summary>
public sealed class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoices;
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInvoiceCalculationService _calculation;
    private readonly IValidator<InvoiceWriteRequest> _validator;
    private readonly IValidator<InvoiceListQuery> _listValidator;
    private readonly ITenantProvider _tenantProvider;
    private readonly ITenantScope _tenantScope;

    /// <summary>ساخت سرویس صورتحساب.</summary>
    public InvoiceService(
        IInvoiceRepository invoices,
        ICustomerRepository customers,
        IUnitOfWork unitOfWork,
        IInvoiceCalculationService calculation,
        IValidator<InvoiceWriteRequest> validator,
        IValidator<InvoiceListQuery> listValidator,
        ITenantProvider tenantProvider,
        ITenantScope tenantScope)
    {
        _invoices = invoices;
        _customers = customers;
        _unitOfWork = unitOfWork;
        _calculation = calculation;
        _validator = validator;
        _listValidator = listValidator;
        _tenantProvider = tenantProvider;
        _tenantScope = tenantScope;
    }

    /// <inheritdoc />
    public async Task<InvoiceViewModel> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.GetWithDetailsAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Invoice), id);
        return Map(invoice, false);
    }

    /// <inheritdoc />
    public async Task<PagedResult<InvoiceViewModel>> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken).ConfigureAwait(false);
        var dataQuery = new InvoiceQuery
        {
            Status = query.Status,
            Type = query.Type,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            CustomerId = query.CustomerId,
            Search = query.Search,
            Skip = query.Skip,
            Take = query.PageSize,
            Descending = query.Descending
        };
        var (items, count) = await _invoices.GetPagedAsync(dataQuery, cancellationToken).ConfigureAwait(false);
        return PagedResult<InvoiceViewModel>.Create(items.Select(x => Map(x, false)).ToArray(), count, query);
    }

    /// <inheritdoc />
    public async Task<InvoiceViewModel> CreateAsync(InvoiceWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var tenantId = _tenantScope.TenantId ?? _tenantProvider.TenantId
            ?? throw new InvalidOperationException("مودی درخواست مشخص نیست؛ ثبت صورتحساب بدون tenant مجاز نیست.");
        var externalId = Normalize(request.ExternalId);

        if (externalId is not null)
        {
            var existing = await _invoices.GetByExternalIdAsync(externalId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return Map(existing, true);
            }
        }

        var customer = request.CustomerId is null
            ? null
            : await _customers.GetByIdAsync(request.CustomerId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Customer), request.CustomerId.Value);

        var invoice = CreateEntity(request, tenantId, externalId, customer);
        _calculation.Apply(invoice, request.Items);
        await _invoices.AddAsync(invoice, cancellationToken).ConfigureAwait(false);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (externalId is not null)
        {
            // Concurrent duplicate: let the database unique index arbitrate, then return the winner.
            var winner = await _invoices.GetByExternalIdAsync(externalId, cancellationToken).ConfigureAwait(false);
            if (winner is not null)
            {
                return Map(winner, true);
            }

            throw;
        }

        return Map(invoice, false);
    }

    private static Invoice CreateEntity(InvoiceWriteRequest request, int tenantId, string? externalId, Customer? customer)
    {
        var invoice = new Invoice
        {
            TenantId = tenantId,
            InvoiceNumber = request.InvoiceNumber.Trim(),
            ExternalId = externalId,
            Type = request.Type,
            PatternCode = request.PatternCode,
            SubjectCode = request.SubjectCode,
            SettlementMethod = request.SettlementMethod,
            Status = InvoiceStatus.Draft,
            IssueDate = request.IssueDate,
            CustomerId = customer?.Id,
            BuyerName = customer?.Name ?? request.BuyerName.Trim(),
            BuyerType = customer?.BuyerType ?? request.BuyerType,
            BuyerNationalId = customer?.NationalId ?? Normalize(request.BuyerNationalId),
            BuyerEconomicCode = customer?.EconomicCode ?? Normalize(request.BuyerEconomicCode),
            BuyerPostalCode = customer?.PostalCode ?? Normalize(request.BuyerPostalCode),
            BuyerAddress = customer?.Address ?? Normalize(request.BuyerAddress),
            BuyerBranchCode = Normalize(request.BuyerBranchCode),
            ReferenceTaxId = Normalize(request.ReferenceTaxId)
        };

        foreach (var payment in request.Payments)
        {
            invoice.Payments.Add(new InvoicePayment
            {
                TenantId = tenantId,
                Method = payment.Method,
                Amount = payment.Amount,
                PaymentDate = payment.PaymentDate,
                ReferenceNumber = Normalize(payment.ReferenceNumber),
                TerminalNumber = Normalize(payment.TerminalNumber),
                AcceptorNumber = Normalize(payment.AcceptorNumber),
                SwitchNumber = Normalize(payment.SwitchNumber),
                PayerNationalId = Normalize(payment.PayerNationalId),
                MaskedCardNumber = Normalize(payment.MaskedCardNumber),
                VatPaymentId = Normalize(payment.VatPaymentId),
                VatBillId = Normalize(payment.VatBillId)
            });
        }

        return invoice;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InvoiceViewModel Map(Invoice invoice, bool replay) => new()
    {
        Id = invoice.Id,
        TenantId = invoice.TenantId,
        InvoiceNumber = invoice.InvoiceNumber,
        ExternalId = invoice.ExternalId,
        TaxId = invoice.TaxId,
        Uid = invoice.Uid,
        Status = invoice.Status,
        BuyerName = invoice.BuyerName,
        TotalBeforeDiscount = invoice.TotalBeforeDiscount,
        TotalDiscount = invoice.TotalDiscount,
        TotalAfterDiscount = invoice.TotalAfterDiscount,
        TotalVat = invoice.TotalVat,
        TotalOtherTaxes = invoice.TotalOtherTaxes,
        TotalAmount = invoice.TotalAmount,
        IsIdempotentReplay = replay
    };
}
