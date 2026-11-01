using FluentAssertions;
using MoudianMiddleware.CoreLayer.Infrastructure.Security;
using MoudianMiddleware.Tests.TestHelpers;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

[Collection("Environment-sensitive tests")]
public sealed class SecretEnvironmentTests
{
    [Fact]
    public void FromEnvironment_LoadsVersionedKeksAndIgnoresConfigurationSources()
    {
        var names = new[] { "MOUDIAN__MASTER_KEY", "MOUDIAN__MASTER_KEY_ACTIVE_VERSION", "MOUDIAN__MASTER_KEY_V1", "MOUDIAN__MASTER_KEY_V2" };
        var originals = names.ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name));
        try
        {
            Environment.SetEnvironmentVariable("MOUDIAN__MASTER_KEY", null);
            Environment.SetEnvironmentVariable("MOUDIAN__MASTER_KEY_ACTIVE_VERSION", "2");
            Environment.SetEnvironmentVariable("MOUDIAN__MASTER_KEY_V1", Convert.ToBase64String(new byte[32]));
            Environment.SetEnvironmentVariable("MOUDIAN__MASTER_KEY_V2", Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
            var options = SecretProtectionOptions.FromEnvironment();
            options.ActiveKeyVersion.Should().Be(2);
            options.KeysBase64.Keys.Should().Contain(1).And.Contain(2);
        }
        finally
        {
            foreach (var (name, value) in originals) Environment.SetEnvironmentVariable(name, value);
        }
    }
}
