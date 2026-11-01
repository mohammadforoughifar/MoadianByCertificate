using FluentAssertions;
using MoudianMiddleware.Api.Services;
using Xunit;

namespace MoudianMiddleware.Tests.Api;

[Collection("Environment-sensitive tests")]
public sealed class JwtOptionsTests
{
    [Fact]
    public void FromEnvironment_UsesEphemeralKeyOnlyWhenDevelopmentFallbackIsAllowed()
    {
        const string variable = "MOUDIAN__JWT_SIGNING_KEY";
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);

            var development = JwtOptions.FromEnvironment(allowDevelopmentFallback: true);
            development.SigningKey.Should().HaveCount(32);

            Action production = () => JwtOptions.FromEnvironment();
            production.Should().Throw<InvalidOperationException>()
                .WithMessage("MOUDIAN__JWT_SIGNING_KEY is required outside Development.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }
}
