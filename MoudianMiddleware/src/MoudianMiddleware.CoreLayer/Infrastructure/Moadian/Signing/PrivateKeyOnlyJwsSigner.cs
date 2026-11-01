using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxCollectData.Library.Abstraction.Cryptography;

namespace MoudianMiddleware.CoreLayer.Infrastructure.Moadian.Signing;

/// <summary>
/// Experimental JWS/RS256 signatory without the x5c header. Server acceptance is not confirmed.
/// </summary>
public sealed class PrivateKeyOnlyJwsSigner : ISignatory, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly RSA _privateKey;
    private bool _disposed;

    /// <summary>Create a JWS signatory which deliberately emits no x5c certificate header.</summary>
    public PrivateKeyOnlyJwsSigner(RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        _privateKey = privateKey;
    }

    /// <inheritdoc />
    public string Sign(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var document = JsonDocument.Parse(text);
        var payload = JsonSerializer.SerializeToUtf8Bytes(document.RootElement, JsonOptions);
        try
        {
            return Encode(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    /// <inheritdoc />
    public string Sign(object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var payload = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        try
        {
            return Encode(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private string Encode(ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["sigT"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["typ"] = "jose",
            ["crit"] = new[] { "sigT" },
            ["cty"] = "text/plain"
        };
        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
        var encodedPayload = Base64UrlEncode(payload);
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");
        byte[]? signature = null;
        try
        {
            signature = _privateKey.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{encodedHeader}.{encodedPayload}.{Base64UrlEncode(signature)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingInput);
            if (signature is not null)
            {
                CryptographicOperations.ZeroMemory(signature);
            }
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _privateKey.Dispose();
        _disposed = true;
    }
}
