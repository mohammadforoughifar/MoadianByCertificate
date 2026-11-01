using System.Security.Cryptography;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoudianMiddleware.Contracts.Enums;
using MoudianMiddleware.Contracts.ViewModels.Moadian;
using MoudianMiddleware.CoreLayer.Infrastructure.Security;
using MoudianMiddleware.CoreLayer.Services.Moadian;
using MoudianMiddleware.CoreLayer.Validators.Moadian;
using MoudianMiddleware.DataLayer.Context;
using MoudianMiddleware.DataLayer.Entities;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianCredentialServiceTests
{
    [Fact]
    public async Task SaveAsync_PrivateKeyOnlyDoesNotRequireCertificateAndPersistsOnlyCiphertextEnvelope()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(1);
        await using var context = database.CreateContext(tenantId: null, tenantScope);
        context.Tenants.Add(new Tenant { Name = "Test tenant" });
        await context.SaveChangesAsync();
        context.MoadianSettings.Add(new MoadianSetting
        {
            TenantId = 1,
            MemoryId = "A11216",
            EconomicCode = "12345678901",
            SignatureMode = MoadianSignatureMode.PrivateKeyOnly,
            IsSignatureExperimentalApproved = true
        });
        await context.SaveChangesAsync();

        var key = RandomNumberGenerator.GetBytes(32);
        var protector = new AesGcmSecretProtector(Options.Create(new SecretProtectionOptions
        {
            ActiveKeyVersion = 1,
            KeysBase64 = new Dictionary<int, string> { [1] = Convert.ToBase64String(key) }
        }));
        var service = new MoadianCredentialService(
            context,
            protector,
            new SaveMoadianCredentialRequestValidator(),
            new FakeTenantProvider(null),
            tenantScope);
        using var rsa = RSA.Create(2048);
        var privateKeyText = rsa.ExportPkcs8PrivateKeyPem();

        var view = await service.SaveAsync(new SaveMoadianCredentialRequest { PrivateKeyPem = privateKeyText });
        var stored = await context.MoadianCredentials.AsNoTracking().SingleAsync();

        view.HasCertificate.Should().BeFalse();
        view.HasPrivateKey.Should().BeTrue();
        stored.CertificateBase64.Should().BeNull();
        stored.EncryptedPrivateKey.Should().NotBe(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(privateKeyText)));
        stored.PrivateKeyNonce.Should().NotBeNullOrWhiteSpace();
        stored.PrivateKeyTag.Should().NotBeNullOrWhiteSpace();
        stored.PrivateKeyKeyVersion.Should().Be(1);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public async Task SaveAsync_RejectsUnapprovedPrivateKeyOnlyMode()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantScope = new TenantScope();
        using var tenantLease = tenantScope.Enter(1);
        await using var context = database.CreateContext(tenantId: null, tenantScope);
        context.Tenants.Add(new Tenant { Name = "Test tenant" });
        await context.SaveChangesAsync();
        context.MoadianSettings.Add(new MoadianSetting
        {
            TenantId = 1,
            MemoryId = "A11216",
            EconomicCode = "12345678901",
            SignatureMode = MoadianSignatureMode.PrivateKeyOnly,
            IsSignatureExperimentalApproved = false
        });
        await context.SaveChangesAsync();

        var protector = new AesGcmSecretProtector(Options.Create(new SecretProtectionOptions
        {
            ActiveKeyVersion = 1,
            KeysBase64 = new Dictionary<int, string> { [1] = Convert.ToBase64String(new byte[32]) }
        }));
        var service = new MoadianCredentialService(
            context,
            protector,
            new SaveMoadianCredentialRequestValidator(),
            new FakeTenantProvider(null),
            tenantScope);

        var act = () => service.SaveAsync(new SaveMoadianCredentialRequest { PrivateKeyPem = "private" });

        await act.Should().ThrowAsync<ValidationException>();
        (await context.MoadianCredentials.CountAsync()).Should().Be(0);
    }
}
