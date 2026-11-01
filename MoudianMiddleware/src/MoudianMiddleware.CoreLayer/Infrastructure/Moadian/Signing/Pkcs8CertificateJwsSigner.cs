using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TaxCollectData.Library.Abstraction.Cryptography;
using TaxCollectData.Library.Factories;
using TaxCollectData.Library.Providers;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;

/// <summary>In-memory adapter to the SDK's confirmed RSA + X.509 signing factory.</summary>
public sealed class Pkcs8CertificateJwsSigner : ISignatory, IDisposable
{
    private readonly RSA _privateKey;
    private readonly X509Certificate2 _certificate;
    private readonly ISignatory _sdkSignatory;
    private bool _disposed;

    /// <summary>Create an SDK signatory while retaining key/certificate only in memory.</summary>
    public Pkcs8CertificateJwsSigner(RSA privateKey, X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(certificate);
        _privateKey = privateKey;
        _certificate = certificate;
        _sdkSignatory = new SignatoryFactory(new CurrentDateProvider()).Create(privateKey, certificate);
    }

    /// <inheritdoc />
    public string Sign(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        return _sdkSignatory.Sign(text);
    }

    /// <inheritdoc />
    public string Sign(object data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);
        return _sdkSignatory.Sign(data);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _privateKey.Dispose();
        _certificate.Dispose();
        _disposed = true;
    }
}
