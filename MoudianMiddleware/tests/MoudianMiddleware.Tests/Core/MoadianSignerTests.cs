using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianSignerTests
{
    [Fact]
    public void PrivateKeyOnlySigner_ProducesVerifiableRs256WithoutX5c()
    {
        using var rsa = RSA.Create(2048);
        using var signer = new PrivateKeyOnlyJwsSigner(rsa);

        var compactJws = signer.Sign("{\"taxid\":\"test-1\",\"optional\":null}");
        var segments = compactJws.Split('.');
        segments.Should().HaveCount(3);
        using var header = JsonDocument.Parse(Decode(segments[0]));
        header.RootElement.GetProperty("alg").GetString().Should().Be("RS256");
        header.RootElement.TryGetProperty("x5c", out _).Should().BeFalse();

        var signingInput = Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}");
        using var publicKey = RSA.Create();
        publicKey.ImportParameters(rsa.ExportParameters(false));
        publicKey.VerifyData(signingInput, Decode(segments[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .Should().BeTrue();
    }

    [Fact]
    public void Pkcs8CertificateAdapter_ProducesSdkJwsWithCertificateChainHeader()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test Taxpayer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var signer = new Pkcs8CertificateJwsSigner(rsa, certificate);

        var compactJws = signer.Sign("{\"taxid\":\"test-2\"}");
        var segments = compactJws.Split('.');
        segments.Should().HaveCount(3);
        using var header = JsonDocument.Parse(Decode(segments[0]));
        header.RootElement.GetProperty("alg").GetString().Should().Be("RS256");
        header.RootElement.GetProperty("x5c")[0].GetString().Should().Be(Convert.ToBase64String(certificate.RawData));

        var signingInput = Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}");
        using var publicKey = certificate.GetRSAPublicKey();
        publicKey.Should().NotBeNull();
        publicKey!.VerifyData(signingInput, Decode(segments[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .Should().BeTrue();
    }

    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }
}
