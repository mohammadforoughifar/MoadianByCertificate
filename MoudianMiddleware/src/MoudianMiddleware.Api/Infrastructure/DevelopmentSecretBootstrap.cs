using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace MoudianMiddleware.Api.Infrastructure;

/// <summary>Creates a stable, per-user local-development KEK without placing secrets in source control.</summary>
public static class DevelopmentSecretBootstrap
{
    public static void EnsureDevelopmentKek(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOUDIAN__MASTER_KEY")))
            return;

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new InvalidOperationException("Could not locate the per-user local application data directory for the Development KEK.");

        var directory = Path.Combine(localData, "MoudianMiddleware", "Development");
        Directory.CreateDirectory(directory);
        var keyPath = Path.Combine(directory, "master-key-v1.base64");
        string encoded;
        if (File.Exists(keyPath))
        {
            encoded = File.ReadAllText(keyPath).Trim();
        }
        else
        {
            encoded = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            try
            {
                using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(encoded);
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                encoded = File.ReadAllText(keyPath).Trim();
            }

        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        byte[] key;
        try { key = Convert.FromBase64String(encoded); }
        catch (FormatException exception) { throw new InvalidOperationException($"The local Development KEK file '{keyPath}' is not valid Base64.", exception); }
        try
        {
            if (key.Length != 32)
                throw new InvalidOperationException($"The local Development KEK file '{keyPath}' must contain exactly 32 bytes.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }

        Environment.SetEnvironmentVariable("MOUDIAN__MASTER_KEY", encoded);
        Console.WriteLine($"Using the persistent Development KEK stored outside the repository: {keyPath}");
    }
}
