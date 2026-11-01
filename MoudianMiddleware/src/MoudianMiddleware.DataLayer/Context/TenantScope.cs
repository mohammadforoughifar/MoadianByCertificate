using MoudianMiddleware.DataLayer.Abstractions;

namespace MoudianMiddleware.DataLayer.Context;

/// <summary>
/// Async-flow-local implementation of <see cref="ITenantScope"/>.
/// Register as scoped; background workers should create one DI scope per tenant operation.
/// </summary>
public sealed class TenantScope : ITenantScope
{
    private readonly AsyncLocal<Frame?> _current = new();

    /// <inheritdoc />
    public int? TenantId => _current.Value?.TenantId;

    /// <inheritdoc />
    public IDisposable Enter(int tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        var previous = _current.Value;
        var frame = new Frame(tenantId, previous);
        _current.Value = frame;
        return new ScopeLease(this, frame);
    }

    private sealed record Frame(int TenantId, Frame? Parent);

    private sealed class ScopeLease : IDisposable
    {
        private readonly TenantScope _owner;
        private readonly Frame _frame;
        private bool _disposed;

        public ScopeLease(TenantScope owner, Frame frame)
        {
            _owner = owner;
            _frame = frame;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(_owner._current.Value, _frame))
            {
                throw new InvalidOperationException("Tenant scopes must be disposed in reverse order in the same asynchronous flow.");
            }

            _owner._current.Value = _frame.Parent;
            _disposed = true;
        }
    }
}
