using FluentValidation;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Customers;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.Contracts.ViewModels.Products;

namespace MoudianMiddleware.CoreLayer.Validators.Common;

internal static class ListQueryRules
{
    public static void Add<TQuery>(AbstractValidator<TQuery> validator) where TQuery : PaginationQuery
    {
        validator.RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(PaginationQuery.MinPageNumber);
        validator.RuleFor(x => x.PageSize).InclusiveBetween(PaginationQuery.MinPageNumber, PaginationQuery.MaxPageSize);
        validator.RuleFor(x => x.Search).MaximumLength(200);
    }
}

/// <summary>اعتبارسنجی فیلتر فهرست کالا.</summary>
public sealed class ProductListQueryValidator : AbstractValidator<ProductListQuery>
{
    /// <summary>ساخت قواعد صفحه‌بندی و جست‌وجوی کالا.</summary>
    public ProductListQueryValidator() => ListQueryRules.Add(this);
}

/// <summary>اعتبارسنجی فیلتر فهرست مشتری.</summary>
public sealed class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    /// <summary>ساخت قواعد صفحه‌بندی و جست‌وجوی مشتری.</summary>
    public CustomerListQueryValidator()
    {
        ListQueryRules.Add(this);
        RuleFor(x => x.BuyerType).IsInEnum().When(x => x.BuyerType.HasValue);
    }
}

/// <summary>اعتبارسنجی فیلتر فهرست صورتحساب.</summary>
public sealed class InvoiceListQueryValidator : AbstractValidator<InvoiceListQuery>
{
    /// <summary>ساخت قواعد صفحه‌بندی، بازه‌ی زمانی و شناسه‌ی مشتری.</summary>
    public InvoiceListQueryValidator()
    {
        ListQueryRules.Add(this);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type.HasValue);
        RuleFor(x => x.CustomerId).GreaterThan(0).When(x => x.CustomerId.HasValue);
        RuleFor(x => x.FromDate).LessThanOrEqualTo(x => x.ToDate).When(x => x.FromDate.HasValue && x.ToDate.HasValue);
    }
}
