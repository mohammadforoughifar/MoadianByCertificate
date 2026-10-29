using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.Entities.Common;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Data;

/// <summary>
/// تست‌های جداسازی داده در حالت چند‌مستأجری و رفتار حذف نرم.
/// </summary>
public sealed class TenantIsolationTests
{
    /// <summary>
    /// هر مستأجر فقط داده‌ی خودش را می‌بیند.
    /// </summary>
    [Fact]
    public async Task QueryFilter_ReturnsOnlyCurrentTenantDataAsync()
    {
        var databaseName = Guid.NewGuid().ToString();

        await using (var tenantOneContext = TestDbContextFactory.Create(databaseName, tenantId: 1))
        {
            await tenantOneContext.Invoices.AddAsync(NewInvoice("INV-1"));
            await tenantOneContext.SaveChangesAsync();
        }

        await using (var tenantTwoContext = TestDbContextFactory.Create(databaseName, tenantId: 2))
        {
            await tenantTwoContext.Invoices.AddAsync(NewInvoice("INV-2"));
            await tenantTwoContext.SaveChangesAsync();
        }

        await using var context = TestDbContextFactory.Create(databaseName, tenantId: 1);
        var invoices = await context.Invoices.ToListAsync();

        invoices.Should().ContainSingle();
        invoices[0].InvoiceNumber.Should().Be("INV-1");
        invoices[0].TenantId.Should().Be(1);
    }

    /// <summary>
    /// اگر مستأجر مشخص نباشد، هیچ داده‌ای بازنمی‌گردد (سیاست رد پیش‌فرض).
    /// </summary>
    [Fact]
    public async Task QueryFilter_WithoutTenant_ReturnsNothingAsync()
    {
        var databaseName = Guid.NewGuid().ToString();

        await using (var context = TestDbContextFactory.Create(databaseName, tenantId: 1))
        {
            await context.Invoices.AddAsync(NewInvoice("INV-1"));
            await context.SaveChangesAsync();
        }

        await using var anonymousContext = TestDbContextFactory.Create(databaseName, tenantId: null);
        var invoices = await anonymousContext.Invoices.ToListAsync();

        invoices.Should().BeEmpty();
    }

    /// <summary>
    /// ثبت رکورد سازمانی بدون مستأجر باید خطا بدهد (جلوگیری از نشت داده).
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithoutTenant_ThrowsAsync()
    {
        await using var context = TestDbContextFactory.Create(Guid.NewGuid().ToString(), tenantId: null);
        await context.Invoices.AddAsync(NewInvoice("INV-X"));

        Func<Task> act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// حذف، به حذف نرم تبدیل می‌شود و رکورد فیزیکی باقی می‌ماند.
    /// </summary>
    [Fact]
    public async Task Remove_ConvertsToSoftDeleteAsync()
    {
        var databaseName = Guid.NewGuid().ToString();

        await using (var context = TestDbContextFactory.Create(databaseName, tenantId: 1))
        {
            var invoice = NewInvoice("INV-SOFT");
            await context.Invoices.AddAsync(invoice);
            await context.SaveChangesAsync();
            context.Invoices.Remove(invoice);
            await context.SaveChangesAsync();
        }

        await using var assertionContext = TestDbContextFactory.Create(databaseName, tenantId: 1);

        (await assertionContext.Invoices.ToListAsync()).Should().BeEmpty();

        var includingDeleted = await assertionContext.Invoices
            .IgnoreQueryFilters()
            .ToListAsync();

        includingDeleted.Should().ContainSingle();
        includingDeleted[0].IsDeleted.Should().BeTrue();
    }

    /// <summary>
    /// همه‌ی موجودیت‌های سازمانی باید فیلتر سراسری داشته باشند؛ افزودن موجودیت جدید بدون فیلتر، این تست را می‌شکند.
    /// </summary>
    [Fact]
    public void AllTenantEntities_HaveQueryFilter()
    {
        using var context = TestDbContextFactory.Create(Guid.NewGuid().ToString(), tenantId: 1);

        var tenantEntityTypes = context.Model
            .GetEntityTypes()
            .Where(x => typeof(ITenantEntity).IsAssignableFrom(x.ClrType))
            .ToList();

        tenantEntityTypes.Should().NotBeEmpty();

        foreach (var entityType in tenantEntityTypes)
        {
            entityType.GetQueryFilter().Should().NotBeNull(
                because: $"موجودیت {entityType.ClrType.Name} باید فیلتر مستأجر داشته باشد");
        }
    }

    private static Invoice NewInvoice(string invoiceNumber) => new()
    {
        InvoiceNumber = invoiceNumber,
        BuyerName = "خریدار آزمایشی",
        IssueDate = FakeClock.DefaultNow,
        PatternCode = 1,
        SubjectCode = 1
    };
}
