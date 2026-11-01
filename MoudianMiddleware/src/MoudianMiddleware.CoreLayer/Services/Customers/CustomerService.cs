using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.Contracts.ViewModels.Customers;
using MoudianMiddleware.CoreLayer.Common.Exceptions;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using MoudianMiddleware.DataLayer.Abstractions.Repositories;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.UnitOfWork;

namespace MoudianMiddleware.CoreLayer.Services.Customers;

/// <summary>مدیریت مشتریان در محدوده‌ی tenant جاری.</summary>
public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CustomerWriteRequest> _validator;
    private readonly IValidator<CustomerListQuery> _listValidator;

    /// <summary>ساخت سرویس مشتری.</summary>
    public CustomerService(
        ICustomerRepository customers,
        IUnitOfWork unitOfWork,
        IValidator<CustomerWriteRequest> validator,
        IValidator<CustomerListQuery> listValidator)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _listValidator = listValidator;
    }

    /// <inheritdoc />
    public async Task<CustomerViewModel> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Customer), id);
        return Map(customer);
    }

    /// <inheritdoc />
    public async Task<PagedResult<CustomerViewModel>> ListAsync(CustomerListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken).ConfigureAwait(false);
        var (items, count) = await _customers.GetPagedAsync(
            query.Search, query.BuyerType, query.IsActive, query.Skip, query.PageSize, cancellationToken).ConfigureAwait(false);
        return PagedResult<CustomerViewModel>.Create(items.Select(Map).ToArray(), count, query);
    }

    /// <inheritdoc />
    public async Task<CustomerViewModel> CreateAsync(CustomerWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var code = request.Code.Trim();
        if (await _customers.ExistsCodeAsync(code, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("کد مشتری در این مودی قبلاً ثبت شده است.");
        }

        var customer = new Customer();
        Apply(customer, request);
        await _customers.AddAsync(customer, cancellationToken).ConfigureAwait(false);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await _customers.ExistsCodeAsync(code, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessConflictException("کد مشتری در این مودی قبلاً ثبت شده است.");
            }

            throw;
        }
        return Map(customer);
    }

    /// <inheritdoc />
    public async Task<CustomerViewModel> UpdateAsync(int id, CustomerWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _validator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var code = request.Code.Trim();
        var customer = await _customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Customer), id);
        if (await _customers.ExistsCodeAsync(code, id, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("کد مشتری در این مودی به رکورد دیگری تخصیص یافته است.");
        }

        Apply(customer, request);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (await _customers.ExistsCodeAsync(code, id, cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessConflictException("کد مشتری در این مودی به رکورد دیگری تخصیص یافته است.");
            }

            throw;
        }
        return Map(customer);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Customer), id);
        _customers.Remove(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Apply(Customer customer, CustomerWriteRequest request)
    {
        customer.Code = request.Code.Trim();
        customer.Name = request.Name.Trim();
        customer.BuyerType = request.BuyerType;
        customer.NationalId = Normalize(request.NationalId);
        customer.EconomicCode = Normalize(request.EconomicCode);
        customer.PostalCode = Normalize(request.PostalCode);
        customer.Address = Normalize(request.Address);
        customer.Phone = Normalize(request.Phone);
        customer.Email = Normalize(request.Email);
        customer.ExternalCode = Normalize(request.ExternalCode);
        customer.IsActive = request.IsActive;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerViewModel Map(Customer customer) => new()
    {
        Id = customer.Id,
        Code = customer.Code,
        Name = customer.Name,
        BuyerType = customer.BuyerType,
        NationalId = customer.NationalId,
        EconomicCode = customer.EconomicCode,
        PostalCode = customer.PostalCode,
        Address = customer.Address,
        Phone = customer.Phone,
        Email = customer.Email,
        ExternalCode = customer.ExternalCode,
        IsActive = customer.IsActive
    };
}
