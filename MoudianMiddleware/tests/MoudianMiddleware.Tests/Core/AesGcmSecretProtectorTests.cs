using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MoudianMiddleware.CoreLayer.Infrastructure.Security;
using MoudianMiddleware.CoreLayer.Interfaces.Infrastructure;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class AesGcmSecretProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsAndBindsTenantAndPurpose()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var options = Options.Create(new SecretProtectionOptions
        {
            ActiveKeyVersion = 4,
            KeysBase64 = new Dictionary<int, string> { [4] = Convert.ToBase64String(key) }
        });
        var protector = new AesGcmSecretProtector(options);
        var plaintext = Encoding.UTF8.GetBytes("pkcs8 test material");

        var protectedSecret = protector.Protect(plaintext, tenantId: 17, purpose: "private-key");
        var decrypted = protector.Unprotect(protectedSecret, tenantId: 17, purpose: "private-key");

        protectedSecret.KeyVersion.Should().Be(4);
        protectedSecret.CiphertextBase64.Should().NotBe(Convert.ToBase64String(plaintext));
        decrypted.Should().Equal(plaintext);
        Action wrongTenant = () => protector.Unprotect(protectedSecret, tenantId: 18, purpose: "private-key");
        Action wrongPurpose = () => protector.Unprotect(protectedSecret, tenantId: 17, purpose: "pkcs11-pin");
        wrongTenant.Should().Throw<CryptographicException>();
        wrongPurpose.Should().Throw<CryptographicException>();

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(decrypted);
    }

    [Fact]
    public void Unprotect_RejectsTamperedAuthenticationTag()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var protector = new AesGcmSecretProtector(Options.Create(new SecretProtectionOptions
        {
            ActiveKeyVersion = 1,
            KeysBase64 = new Dictionary<int, string> { [1] = Convert.ToBase64String(key) }
        }));
        var original = protector.Protect("secret"u8, tenantId: 3, purpose: "credential");
        var tag = Convert.FromBase64String(original.TagBase64);
        tag[0] ^= 0x80;
        var tampered = original with { TagBase64 = Convert.ToBase64String(tag) };

        Action act = () => protector.Unprotect(tampered, tenantId: 3, purpose: "credential");

        act.Should().Throw<CryptographicException>();
        CryptographicOperations.ZeroMemory(key);
    }
}
