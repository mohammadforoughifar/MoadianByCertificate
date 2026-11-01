using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Products;
using MoudianMiddleware.CoreLayer.Common.Exceptions;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.UnitOfWork;

namespace MoudianMiddleware.CoreLayer.Services.Products;

/// <summary>مدیریت کالا/خدمت با اعتبارسنجی و محدودیت مودی repository.</summary>
public sealed class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<ProductWriteRequest> _validator;
    private readonly IValidator<ProductListQuery> _listValidator;

    /// <summary>ساخت سرویس کالا.</summary>
    public ProductService(
        IProductRepository products,
        IUnitOfWork unitOfWork,
        IValidator<ProductWriteRequest> validator,
        IValidator<ProductListQuery> listValidator)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _listValidator = listValidator;
    }

    /// <inheritdoc />
    public async Task<ProductViewModel> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetWithUnitAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Product), id);
        return Map(product);
    }

    /// <inheritdoc />
    public async Task<PagedResult<ProductViewModel>> ListAsync(ProductListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken).ConfigureAwait(false);
        var (items, count) = await _products.GetPagedAsync(query.Search, query.IsActive, query.Skip, query.PageSize, cancellationToken)
            .ConfigureAwait(false);
        return PagedResult<ProductViewModel>.Create(items.Select(Map).ToArray(), count, query);
    }

    /// <inheritdoc />
    public async Task<ProductViewModel> CreateAsync(ProductWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var sstid = request.Sstid.Trim();
        if (await _products.ExistsSstidAsync(sstid, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("شناسه‌ی کالا/خدمت در این مودی قبلاً ثبت شده است.");
        }

        var product = new Product();
        Apply(product, request);
        await _products.AddAsync(product, cancellationToken).ConfigureAwait(false);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await _products.ExistsSstidAsync(sstid, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessConflictException("شناسه‌ی کالا/خدمت در این مودی قبلاً ثبت شده است.");
            }

            throw;
        }

        var saved = await _products.GetWithUnitAsync(product.Id, cancellationToken).ConfigureAwait(false) ?? product;
        return Map(saved);
    }

    /// <inheritdoc />
    public async Task<ProductViewModel> UpdateAsync(int id, ProductWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var sstid = request.Sstid.Trim();
        var product = await _products.GetWithUnitAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Product), id);
        if (await _products.ExistsSstidAsync(sstid, id, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("شناسه‌ی کالا/خدمت در این مودی قبلاً به رکورد دیگری تخصیص یافته است.");
        }

        Apply(product, request);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await _products.ExistsSstidAsync(sstid, id, cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessConflictException("شناسه‌ی کالا/خدمت در این مودی به رکورد دیگری تخصیص یافته است.");
            }

            throw;
        }

        var saved = await _products.GetWithUnitAsync(product.Id, cancellationToken).ConfigureAwait(false) ?? product;
        return Map(saved);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Product), id);
        _products.Remove(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Apply(Product product, ProductWriteRequest request)
    {
        product.Sstid = request.Sstid.Trim();
        product.Title = request.Title.Trim();
        product.MeasuringUnitId = request.MeasuringUnitId;
        product.UnitPrice = request.UnitPrice;
        product.VatRate = request.VatRate;
        product.IsService = request.IsService;
        product.HsCode = request.HsCode?.Trim();
        product.Description = request.Description?.Trim();
        product.IsActive = request.IsActive;
    }

    private static ProductViewModel Map(Product product) => new()
    {
        Id = product.Id,
        TenantId = product.TenantId,
        Sstid = product.Sstid,
        Title = product.Title,
        MeasuringUnitId = product.MeasuringUnitId,
        MeasuringUnitCode = product.MeasuringUnit?.Code,
        UnitPrice = product.UnitPrice,
        VatRate = product.VatRate,
        IsService = product.IsService,
        HsCode = product.HsCode,
        Description = product.Description,
        IsActive = product.IsActive
    };
}
