using System.Text.Json;
using FluentAssertions;
using MoudianMiddleware.CoreLayer.Infrastructure.Moadian;
using Xunit;

namespace MoudianMiddleware.Tests.Core;

public sealed class MoadianPayloadScrubberTests
{
    private readonly MoadianPayloadScrubber _scrubber = new();

    [Fact]
    public void ScrubJson_PreservesAllowlistedReceiptMetadata_AndRedactsPayloadFields()
    {
        const string input = """
            {"uid":"uid-123","referenceNumber":"ref-456","status":"SUCCESS","data":{"buyerNationalId":"1234567890","pin":"9876","body":[{"sstt":"secret item"}]}}
            """;

        var output = _scrubber.ScrubJson(input);
        using var json = JsonDocument.Parse(output);

        json.RootElement.GetProperty("uid").GetString().Should().Be("uid-123");
        json.RootElement.GetProperty("referenceNumber").GetString().Should().Be("ref-456");
        json.RootElement.GetProperty("status").GetString().Should().Be("SUCCESS");
        json.RootElement.GetProperty("data").GetString().Should().Be("[REDACTED]");
        output.Should().NotContain("1234567890").And.NotContain("9876").And.NotContain("secret item");
    }

    [Fact]
    public void ScrubJson_NeverReturnsMalformedRawBody()
    {
        const string rawResponse = "private key = sensitive-value";

        var output = _scrubber.ScrubJson(rawResponse);

        using var json = JsonDocument.Parse(output);
        json.RootElement.GetProperty("redacted").GetBoolean().Should().BeTrue();
        output.Should().NotContain(rawResponse);
    }
}
