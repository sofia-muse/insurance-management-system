using FluentAssertions;

namespace InsuranceManagement.Tests;

public class SecurityGuardrailTests
{
    [Theory]
    [InlineData("InsuranceManagement.Infrastructure/Services/CustomerService.cs")]
    [InlineData("InsuranceManagement.Infrastructure/Services/PolicyService.cs")]
    public void ServiceLayer_DoesNotUseUnsafeRawSqlPatterns(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue();

        var content = File.ReadAllText(path);
        content.Should().NotContain("FromSqlRaw(");
        content.Should().NotContain("ExecuteSqlRaw(");
        content.Should().NotContain("SqlQueryRaw(");
    }
}
