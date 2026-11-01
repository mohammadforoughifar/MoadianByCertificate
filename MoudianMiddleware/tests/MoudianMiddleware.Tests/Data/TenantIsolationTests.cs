using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.DataLayer.Entities.Common;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Data;

/// <summary>Relational tests for tenant filters, soft-delete behavior, and actual unique indexes.</summary>
public sealed class TenantIsolationTests
{
    /// <summary>Only rows for the current tenant are visible through global filters.</summary>
    [Fact]
    public async Task QueryFilter_ReturnsOnlyCurrentTenantDataAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var tenantOne = database.CreateContext(1))
        {
            await tenantOne.Invoices.AddAsync(NewInvoice("INV-1"));
            await tenantOne.SaveChangesAsync();
        }

        await using (var tenantTwo = database.CreateContext(2))
        {
            await tenantTwo.Invoices.AddAsync(NewInvoice("INV-2"));
            await tenantTwo.SaveChangesAsync();
        }

        await using var context = database.CreateContext(1);
        var invoices = await context.Invoices.ToListAsync();
        invoices.Should().ContainSingle();
        invoices[0].InvoiceNumber.Should().Be("INV-1");
        invoices[0].TenantId.Should().Be(1);
    }

    /// <summary>An HTTP-like context with no tenant is default-deny.</summary>
    [Fact]
    public async Task QueryFilter_WithoutTenant_ReturnsNothingAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var context = database.CreateContext(1))
        {
            await context.Invoices.AddAsync(NewInvoice("INV-1"));
            await context.SaveChangesAsync();
        }

        await using var anonymousContext = database.CreateContext(null);
        (await anonymousContext.Invoices.ToListAsync()).Should().BeEmpty();
    }

    /// <summary>The scoped explicit tenant is applied by the same filter and save interceptor.</summary>
    [Fact]
    public async Task ExplicitTenantScope_StampsAndFiltersWithoutDisablingFiltersAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var explicitScope = new TenantScope();
        using var lease = explicitScope.Enter(27);
        await using var context = database.CreateContext(tenantId: null, explicitScope);
        await context.Invoices.AddAsync(NewInvoice("WORKER-1"));
        await context.SaveChangesAsync();

        var visible = await context.Invoices.ToListAsync();
        visible.Should().ContainSingle();
        visible[0].TenantId.Should().Be(27);
    }

    /// <summary>Tenant-owned inserts without HTTP or explicit tenant context are refused.</summary>
    [Fact]
    public async Task SaveChanges_WithoutTenant_ThrowsAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext(tenantId: null);
        await context.Invoices.AddAsync(NewInvoice("INV-X"));
        Func<Task> act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>EF Core's relational provider executes soft-delete filters and preserves the row.</summary>
    [Fact]
    public async Task Remove_ConvertsToSoftDeleteAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var context = database.CreateContext(1))
        {
            var invoice = NewInvoice("INV-SOFT");
            await context.Invoices.AddAsync(invoice);
            await context.SaveChangesAsync();
            context.Invoices.Remove(invoice);
            await context.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext(1);
        (await assertionContext.Invoices.ToListAsync()).Should().BeEmpty();
        var includingDeleted = await assertionContext.Invoices.IgnoreQueryFilters().ToListAsync();
        includingDeleted.Should().ContainSingle();
        includingDeleted[0].IsDeleted.Should().BeTrue();
    }

    /// <summary>Unique external IDs are enforced by SQLite per tenant, not by an in-memory provider.</summary>
    [Fact]
    public async Task UniqueExternalId_IsEnforcedPerTenantAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using (var tenantOne = database.CreateContext(1))
        {
            var first = NewInvoice("TENANT-1-A");
            first.ExternalId = "ERP-100";
            await tenantOne.Invoices.AddAsync(first);
            await tenantOne.SaveChangesAsync();
        }

        await using (var tenantOneDuplicate = database.CreateContext(1))
        {
            var duplicate = NewInvoice("TENANT-1-B");
            duplicate.ExternalId = "ERP-100";
            await tenantOneDuplicate.Invoices.AddAsync(duplicate);
            Func<Task> duplicateSave = () => tenantOneDuplicate.SaveChangesAsync();
            await duplicateSave.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var tenantTwo = database.CreateContext(2))
        {
            var sameExternalIdForOtherTenant = NewInvoice("TENANT-2-A");
            sameExternalIdForOtherTenant.ExternalId = "ERP-100";
            await tenantTwo.Invoices.AddAsync(sameExternalIdForOtherTenant);
            await tenantTwo.SaveChangesAsync();
        }
    }

    /// <summary>Every ITenantEntity in the EF model has a global query filter.</summary>
    [Fact]
    public async Task AllTenantEntities_HaveQueryFilterAsync()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var context = database.CreateContext(1);
        var tenantEntityTypes = context.Model.GetEntityTypes()
            .Where(x => typeof(ITenantEntity).IsAssignableFrom(x.ClrType))
            .ToList();

        tenantEntityTypes.Should().NotBeEmpty();
        foreach (var entityType in tenantEntityTypes)
        {
            entityType.GetQueryFilter().Should().NotBeNull(
                because: $"موجودیت {entityType.ClrType.Name} باید فیلتر tenant داشته باشد");
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
