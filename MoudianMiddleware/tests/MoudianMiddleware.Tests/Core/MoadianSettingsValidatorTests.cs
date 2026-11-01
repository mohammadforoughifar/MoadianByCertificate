using FluentAssertions;
using FluentValidation.TestHelper;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.CoreLayer.Validators.Moadian;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianSettingsValidatorTests
{
    private readonly MoadianSettingWriteRequestValidator _validator = new();

    [Fact]
    public void SandboxConfiguration_RequiresExplicitBaseUrl()
    {
        var request = new MoadianSettingWriteRequest
        {
            MemoryId = "A11216",
            EconomicCode = "12345678901",
            ActiveEnvironment = MoadianEnvironment.Sandbox
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.SandboxBaseUrl);
    }

    [Fact]
    public void ProductionConfiguration_RejectsHttpAndUserInfoUrls()
    {
        var request = new MoadianSettingWriteRequest
        {
            MemoryId = "A11216",
            EconomicCode = "12345678901",
            ActiveEnvironment = MoadianEnvironment.Production,
            ProductionBaseUrl = "http://user:password@tax.example/api"
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProductionBaseUrl);
    }

    [Fact]
    public void PrivateKeyOnly_RequiresExplicitExperimentalApproval()
    {
        var request = new MoadianSettingWriteRequest
        {
            MemoryId = "A11216",
            EconomicCode = "12345678901",
            SignatureMode = MoadianSignatureMode.PrivateKeyOnly,
            ActiveEnvironment = MoadianEnvironment.Production
        };

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.IsSignatureExperimentalApproved);
    }

}
