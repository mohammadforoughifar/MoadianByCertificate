using Xunit;

namespace MoudianMiddleware.Tests.TestHelpers;

/// <summary>Skips SQL Server concurrency tests unless an explicit disposable-test-server connection is configured.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOUDIAN_TEST_SQLSERVER")))
            Skip = "MOUDIAN_TEST_SQLSERVER is not set; SQLite remains the fast relational test provider.";
    }
}
