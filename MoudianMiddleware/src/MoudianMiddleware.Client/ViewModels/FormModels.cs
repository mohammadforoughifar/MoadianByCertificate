using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Customers;
using MoudianMiddleware.Contracts.ViewModels.Products;

namespace MoudianMiddleware.Client.ViewModels;

public sealed class CustomerFormModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BuyerType BuyerType { get; set; } = BuyerType.NaturalPerson;
    public string? NationalId { get; set; }
    public string? EconomicCode { get; set; }
    public string? PostalCode { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ExternalCode { get; set; }
    public bool IsActive { get; set; } = true;
    public CustomerWriteRequest ToRequest() => new() { Code=Code,Name=Name,BuyerType=BuyerType,NationalId=NationalId,EconomicCode=EconomicCode,PostalCode=PostalCode,Address=Address,Phone=Phone,Email=Email,ExternalCode=ExternalCode,IsActive=IsActive };
}

public sealed class ProductFormModel
{
    public string Sstid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int MeasuringUnitId { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public bool IsService { get; set; }
    public string? HsCode { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ProductWriteRequest ToRequest() => new() { Sstid=Sstid,Title=Title,MeasuringUnitId=MeasuringUnitId,UnitPrice=UnitPrice,VatRate=VatRate,IsService=IsService,HsCode=HsCode,Description=Description,IsActive=IsActive };
}
