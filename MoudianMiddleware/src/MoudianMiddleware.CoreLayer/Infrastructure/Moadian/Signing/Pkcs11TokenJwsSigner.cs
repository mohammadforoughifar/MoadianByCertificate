using TaxCollectData.Library.Abstraction.Cryptography;
using TaxCollectData.Library.Factories;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;

/// <summary>Adapter to the SDK's PKCS#11 signatory factory.</summary>
/// <remarks>The SDK exposes no disposal contract for its token signatory and retains the PIN internally.</remarks>
public sealed class Pkcs11TokenJwsSigner : ISignatory
{
    private readonly ISignatory _sdkSignatory;

    /// <summary>Create a token-backed signatory using library path, certificate alias, and transient PIN.</summary>
    public Pkcs11TokenJwsSigner(string certificateAlias, string libraryPath, string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        _sdkSignatory = new Pkcs11SignatoryFactory().Create(certificateAlias, libraryPath, pin);
    }

    /// <inheritdoc />
    public string Sign(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _sdkSignatory.Sign(text);
    }

    /// <inheritdoc />
    public string Sign(object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return _sdkSignatory.Sign(data);
    }
}
