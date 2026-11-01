using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MoudianMiddleware.Contracts.ViewModels.Invoices;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.CoreLayer;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using MoudianMiddleware.CoreLayer.Interfaces.Services;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class CoreLayerRegistrationTests
{
    [Fact]
    public void AddCoreLayer_RegistersAllCurrentStageFourServiceAndValidatorContracts()
    {
        var services = new ServiceCollection();
        services.AddCoreLayer();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IValidator<InvoiceWriteRequest>>().Should().NotBeNull();
        provider.GetRequiredService<IValidator<InvoiceListQuery>>().Should().NotBeNull();
        provider.GetRequiredService<IValidator<MoadianSettingWriteRequest>>().Should().NotBeNull();
        provider.GetRequiredService<IValidator<SaveMoadianCredentialRequest>>().Should().NotBeNull();
        services.Should().Contain(x => x.ServiceType == typeof(IMoadianSettingService));
        services.Should().Contain(x => x.ServiceType == typeof(IMoadianCredentialService));
        services.Should().Contain(x => x.ServiceType == typeof(IMoadianSubmissionService));
        services.Should().Contain(x => x.ServiceType == typeof(IMoadianGateway));
    }
}
