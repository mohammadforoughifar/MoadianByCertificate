using System.Text.Json;
using FluentAssertions;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using MoudianMiddleware.DataLayer.Entities;
using TaxCollectData.Library.Configs;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianInvoiceMapperTests
{
    [Fact]
    public void Map_PreservesEntityMoneyAndDecimalTypesAtTheSingleSdkBoundary()
    {
        var issueDate = new DateTimeOffset(2026, 10, 31, 12, 30, 0, TimeSpan.Zero);
        var setting = new MoadianSetting { MemoryId = "A11216", EconomicCode = "12345678901" };
        var invoice = new Invoice
        {
            TaxId = "tax-id-001",
            IssueDate = issueDate,
            Type = InvoiceType.Original,
            PatternCode = 1,
            SubjectCode = 1,
            SettlementMethod = SettlementMethod.Cash,
            BuyerType = BuyerType.NaturalPerson,
            TotalBeforeDiscount = 150,
            TotalDiscount = 10,
            TotalAfterDiscount = 140,
            TotalVat = 14,
            TotalAmount = 154,
            CashPayment = 154,
            Items = new List<InvoiceItem>
            {
                new()
                {
                    RowNumber = 1,
                    Sstid = "goods-1",
                    Description = "نمونه",
                    MeasuringUnitCode = "EA",
                    Quantity = 1.5m,
                    UnitPrice = 100.25m,
                    PriceBeforeDiscount = 150,
                    DiscountAmount = 10,
                    PriceAfterDiscount = 140,
                    VatRate = 10m,
                    VatAmount = 14,
                    TotalAmount = 154
                }
            },
            Payments = new List<InvoicePayment>
            {
                new() { Method = PaymentMethod.Cash, Amount = 154 }
            }
        };

        var dto = MoadianInvoiceMapper.Map(setting, invoice);

        dto.Header.taxid.Should().Be(invoice.TaxId);
        dto.Header.indatim.Should().Be(issueDate.ToUnixTimeMilliseconds());
        dto.Header.tbill.Should().Be(154L);
        dto.Body.Should().ContainSingle();
        dto.Body[0].am.Should().Be(1.5m);
        dto.Body[0].fee.Should().Be(100.25m);
        dto.Body[0].prdis.Should().Be(150L);
        dto.Body[0].cut.Should().BeNull("the current entity stores Rial prices, not a foreign currency");
        dto.Payments.Should().ContainSingle();
        dto.Payments[0].pv.Should().Be(154L);

        var serialized = JsonSerializer.Serialize(dto, JsonSerializerConfig.JsonSerializerOptions);
        using var document = JsonDocument.Parse(serialized);
        document.RootElement.GetProperty("body")[0].TryGetProperty("cut", out var cut).Should().BeTrue();
        cut.ValueKind.Should().Be(JsonValueKind.Null,
            "SDK 2.0.29 does not configure null omission, so optional null fields remain in JSON");
    }

    [Fact]
    public void Map_RejectsInvoiceWithoutTaxId()
    {
        var act = () => MoadianInvoiceMapper.Map(new MoadianSetting(), new Invoice());
        act.Should().Throw<InvalidOperationException>();
    }
}
