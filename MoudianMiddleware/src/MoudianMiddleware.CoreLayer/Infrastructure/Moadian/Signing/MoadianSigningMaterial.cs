using TaxCollectData.Library.Abstraction.Cryptography;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;

/// <summary>An SDK signatory plus its owner so ephemeral in-memory key material can be disposed.</summary>
public sealed class MoadianSigningMaterial : IDisposable
{
    private readonly IDisposable? _owner;
    private bool _disposed;

    /// <summary>Create a signing material wrapper.</summary>
    public MoadianSigningMaterial(ISignatory signatory, IDisposable? owner = null)
    {
        ArgumentNullException.ThrowIfNull(signatory);
        Signatory = signatory;
        _owner = owner;
    }

    /// <summary>The SDK-compatible signing implementation.</summary>
    public ISignatory Signatory { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner?.Dispose();
        _disposed = true;
    }
}
